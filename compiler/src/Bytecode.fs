/// Bytecode format v1: superseding format v0's Phase 1 round-trip proof
/// (`vm/src/bytecode.hpp`, `docs/PLAN-0.1.md` Phase 1) now that `Codegen`
/// (Phase 5) produces something worth defining a real format for. `vm/`
/// doesn't understand v1 yet -- rebuilding it to do so is Phase 6's job,
/// same relationship v0 had to Phase 1 (compiler writes, VM reads, one
/// phase ahead of the other by design).
///
/// `Instruction`/`Chunk` are the *structured*, in-memory representation
/// `Codegen` builds and `Disassemble.fs` prints directly -- jump targets
/// are plain instruction-array indices here, not byte offsets, which is
/// what makes both codegen (no manual byte-offset patching) and the
/// disassembler (no re-decoding) simple. `write` is the only place that
/// cares about the actual on-disk byte layout, converting indices to byte
/// offsets in one pass right before serializing.
///
/// On-disk layout (all multi-byte integers little-endian, unchanged
/// framing style from v0):
///
///   offset  size  field
///   0       4     magic: 'I' 'Q' 'B' 'C'
///   4       1     format version (0x01 for v1)
///   5       <chunk>   the top-level "script" chunk (see below), treated
///                     as an implicit zero-arity, zero-upvalue function
///
///   <chunk> serialization:
///     4     constant_count (u32)
///     for each constant:
///       1     tag (0x00 number, 0x01 string, 0x02 function)
///       if number:   8   f64
///       if string:   4+N u32 length, UTF-8 bytes
///       if function: name (u32 length + UTF-8 bytes), 1 arity (u8),
///                    2 local_count (u16), 2 upvalue_count (u16),
///                    upvalue_count * (1 from_enclosing_local (u8) +
///                    2 index (u16)), then a nested <chunk>
///     4     code_length (u32) -- byte length of the instruction stream
///     N     instruction bytes
///
///   Instruction encoding: 1 opcode byte, then a fixed operand width
///   determined entirely by the opcode (no variable-width operands except
///   CLOSURE's trailing per-upvalue list, whose own length is always
///   computable from its upvalue_count operand):
///     no operand                                  : 1 byte total
///     one u16 operand (index/slot/count)          : 3 bytes total
///     one u16 byte-offset operand (jumps)         : 3 bytes total
///     CLOSURE <u16 function index> <u16 upvalue
///       count> <upvalue count * 3 bytes>          : 5 + 3*N bytes
module Iqalox.Bytecode

open System.IO
open System.Text
open Iqalox.Bound

[<Literal>]
let FormatVersion = 1uy

type Instruction =
    | Constant of index: int
    | Nil
    | True
    | False
    | Undef
    | Pop
    | PopN of count: int
    | GetLocal of slot: int
    | SetLocal of slot: int
    | GetUpvalue of index: int
    | SetUpvalue of index: int
    | GetGlobal of nameIndex: int
    | SetGlobal of nameIndex: int
    | DefineGlobal of nameIndex: int
    | Add
    | Subtract
    | Multiply
    | Divide
    | Modulo
    | Power
    | Negate
    | Not
    | Equal
    | NotEqual
    | Greater
    | GreaterEqual
    | Less
    | LessEqual
    /// Target is an index into the *same* instruction array, resolved to
    /// a byte offset only when serialized.
    | Jump of target: int
    /// Peeks (doesn't pop) the top of stack; jumps if it's falsy.
    | JumpIfFalse of target: int
    /// Peeks (doesn't pop) the top of stack; jumps if it's anything other
    /// than `nil` -- the primitive `??`'s short-circuiting is built from.
    | JumpIfNotNil of target: int
    | BuildVector of count: int
    | Call of argCount: int
    | Closure of functionIndex: int * upvalues: UpvalueDescriptor list
    | Return
    | Class of nameIndex: int
    | Method of nameIndex: int
    | Inherit
    | GetProperty of nameIndex: int
    | SetProperty of nameIndex: int
    | GetSuper of nameIndex: int

type Constant =
    | NumberConstant of float
    | StringConstant of string
    | FunctionConstant of FunctionProto

and FunctionProto =
    { Name: string
      Arity: int
      LocalCount: int
      Upvalues: UpvalueDescriptor list
      Chunk: Chunk }

and Chunk = { Constants: Constant[]; Code: Instruction[] }

let private opcodeOf =
    function
    | Constant _ -> 0x01uy
    | Nil -> 0x02uy
    | True -> 0x03uy
    | False -> 0x04uy
    | Undef -> 0x05uy
    | Pop -> 0x06uy
    | PopN _ -> 0x07uy
    | GetLocal _ -> 0x08uy
    | SetLocal _ -> 0x09uy
    | GetUpvalue _ -> 0x0Auy
    | SetUpvalue _ -> 0x0Buy
    | GetGlobal _ -> 0x0Cuy
    | SetGlobal _ -> 0x0Duy
    | DefineGlobal _ -> 0x0Euy
    | Add -> 0x0Fuy
    | Subtract -> 0x10uy
    | Multiply -> 0x11uy
    | Divide -> 0x12uy
    | Modulo -> 0x13uy
    | Power -> 0x14uy
    | Negate -> 0x15uy
    | Not -> 0x16uy
    | Equal -> 0x17uy
    | NotEqual -> 0x18uy
    | Greater -> 0x19uy
    | GreaterEqual -> 0x1Auy
    | Less -> 0x1Buy
    | LessEqual -> 0x1Cuy
    | Jump _ -> 0x1Duy
    | JumpIfFalse _ -> 0x1Euy
    | JumpIfNotNil _ -> 0x1Fuy
    | BuildVector _ -> 0x20uy
    | Call _ -> 0x21uy
    | Closure _ -> 0x22uy
    | Return -> 0x23uy
    | Class _ -> 0x24uy
    | Method _ -> 0x25uy
    | Inherit -> 0x26uy
    | GetProperty _ -> 0x27uy
    | SetProperty _ -> 0x28uy
    | GetSuper _ -> 0x29uy

/// Serialized byte length of one instruction -- see the module doc
/// comment's "Instruction encoding" table.
let instructionByteLength (instruction: Instruction) : int =
    match instruction with
    | Nil
    | True
    | False
    | Undef
    | Pop
    | Add
    | Subtract
    | Multiply
    | Divide
    | Modulo
    | Power
    | Negate
    | Not
    | Equal
    | NotEqual
    | Greater
    | GreaterEqual
    | Less
    | LessEqual
    | Return
    | Inherit -> 1
    | Closure(_, upvalues) -> 1 + 2 + 2 + (3 * List.length upvalues)
    | _ -> 3

/// Byte offset of the start of each instruction, indexed the same as
/// `code` -- the bridge between codegen's index-based jump targets and
/// the file format's byte-offset ones.
let private byteOffsets (code: Instruction[]) : int[] =
    let offsets = Array.zeroCreate code.Length
    let mutable offset = 0
    for i in 0 .. code.Length - 1 do
        offsets.[i] <- offset
        offset <- offset + instructionByteLength code.[i]
    offsets

let rec private writeChunk (writer: BinaryWriter) (chunk: Chunk) : unit =
    writer.Write(uint32 chunk.Constants.Length)
    for constant in chunk.Constants do
        match constant with
        | NumberConstant n ->
            writer.Write(0x00uy)
            writer.Write(n: float)
        | StringConstant s ->
            writer.Write(0x01uy)
            let bytes = Encoding.UTF8.GetBytes(s: string)
            writer.Write(uint32 bytes.Length)
            writer.Write(bytes)
        | FunctionConstant proto ->
            writer.Write(0x02uy)
            let nameBytes = Encoding.UTF8.GetBytes(proto.Name)
            writer.Write(uint32 nameBytes.Length)
            writer.Write(nameBytes)
            writer.Write(byte proto.Arity)
            writer.Write(uint16 proto.LocalCount)
            writer.Write(uint16 (List.length proto.Upvalues))
            for upvalue in proto.Upvalues do
                writer.Write(if upvalue.FromEnclosingLocal then 1uy else 0uy)
                writer.Write(uint16 upvalue.Index)
            writeChunk writer proto.Chunk

    let offsets = byteOffsets chunk.Code

    let targetOffset (index: int) =
        if index < offsets.Length then offsets.[index]
        // A jump targeting one-past-the-end (the common case for "jump to
        // just after the last instruction") has no array entry to read --
        // it's the total code length instead.
        else offsets.[offsets.Length - 1] + instructionByteLength chunk.Code.[chunk.Code.Length - 1]

    use codeStream = new MemoryStream()
    use codeWriter = new BinaryWriter(codeStream)
    for instruction in chunk.Code do
        codeWriter.Write(opcodeOf instruction)
        match instruction with
        | Nil
        | True
        | False
        | Undef
        | Pop
        | Add
        | Subtract
        | Multiply
        | Divide
        | Modulo
        | Power
        | Negate
        | Not
        | Equal
        | NotEqual
        | Greater
        | GreaterEqual
        | Less
        | LessEqual
        | Return
        | Inherit -> ()
        | Constant i
        | PopN i
        | GetLocal i
        | SetLocal i
        | GetUpvalue i
        | SetUpvalue i
        | GetGlobal i
        | SetGlobal i
        | DefineGlobal i
        | BuildVector i
        | Call i
        | Class i
        | Method i
        | GetProperty i
        | SetProperty i
        | GetSuper i -> codeWriter.Write(uint16 i)
        | Jump target
        | JumpIfFalse target
        | JumpIfNotNil target -> codeWriter.Write(uint16 (targetOffset target))
        | Closure(functionIndex, upvalues) ->
            codeWriter.Write(uint16 functionIndex)
            codeWriter.Write(uint16 (List.length upvalues))
            for upvalue in upvalues do
                codeWriter.Write(if upvalue.FromEnclosingLocal then 1uy else 0uy)
                codeWriter.Write(uint16 upvalue.Index)

    let codeBytes = codeStream.ToArray()
    writer.Write(uint32 codeBytes.Length)
    writer.Write(codeBytes)

/// Serializes `chunk` (the top-level script) to `path` in bytecode format
/// v1.
let write (chunk: Chunk) (path: string) : unit =
    use stream = File.Open(path, FileMode.Create, FileAccess.Write)
    use writer = new BinaryWriter(stream)
    writer.Write(Encoding.ASCII.GetBytes("IQBC"))
    writer.Write(FormatVersion)
    writeChunk writer chunk

/// Bytecode format v0 writer -- the F#-side counterpart to vm/src/bytecode.hpp's
/// reader. See that file for the authoritative layout description; kept here
/// too since drift between the two would silently break the round trip.
///
/// Layout (all multi-byte integers little-endian):
///
///   offset  size  field
///   0       4     magic: 'I' 'Q' 'B' 'C'
///   4       1     format version (0x00 for v0)
///   5       4     constant_count (u32)
///           for each constant:
///             1     tag (0x00 = UTF-8 string; only tag defined in v0)
///             4     length (u32)
///             N     UTF-8 bytes
///           4     code_length (u32) -- byte length of the instruction stream
///           N     instruction bytes
///
/// Opcodes (v0):
///   0x01 CONST_STRING <u32 constant index>  push constants.[index]
///   0x02 PRINT                              pop one value, print + newline
///   0xFF HALT                               stop execution
///
/// Relies on the host being little-endian (true of every platform .NET
/// currently targets) -- BinaryWriter's fixed-width integer writes aren't
/// documented as endian-stable, so this is a real, if low-risk, assumption.
module Iqalox.Bytecode

open System.IO
open System.Text

[<Literal>]
let FormatVersion = 0uy

[<Literal>]
let StringTag = 0x00uy

type OpCode =
    | ConstString = 0x01uy
    | Print = 0x02uy
    | Halt = 0xFFuy

type Chunk = { Constants: string list; Code: byte[] }

/// Builds the instruction bytes for `CONST_STRING <index>`.
let constStringInstr (index: uint32) : byte[] =
    let indexBytes = System.BitConverter.GetBytes(index)
    Array.append [| byte OpCode.ConstString |] indexBytes

let printInstr: byte[] = [| byte OpCode.Print |]
let haltInstr: byte[] = [| byte OpCode.Halt |]

/// Serializes `chunk` to `path` in bytecode format v0.
let write (chunk: Chunk) (path: string) : unit =
    use stream = File.Open(path, FileMode.Create, FileAccess.Write)
    use writer = new BinaryWriter(stream)

    writer.Write(Encoding.ASCII.GetBytes("IQBC"))
    writer.Write(FormatVersion)

    writer.Write(uint32 (List.length chunk.Constants))
    for constant in chunk.Constants do
        let bytes = Encoding.UTF8.GetBytes(constant)
        writer.Write(StringTag)
        writer.Write(uint32 bytes.Length)
        writer.Write(bytes)

    writer.Write(uint32 chunk.Code.Length)
    writer.Write(chunk.Code)

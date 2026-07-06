module BytecodeTests

open System
open System.IO
open Xunit
open Iqalox.Bound
open Iqalox.Bytecode

let private tempPath () =
    Path.Combine(Path.GetTempPath(), $"iqaloxc_test_{Guid.NewGuid()}.iqbc")

let private writeAndRead (chunk: Chunk) : byte[] =
    let path = tempPath ()
    try
        write chunk path
        File.ReadAllBytes path
    finally
        File.Delete path

let private pushU32 (bytes: ResizeArray<byte>) (value: uint32) = bytes.AddRange(BitConverter.GetBytes(value))
let private pushU16 (bytes: ResizeArray<byte>) (value: uint16) = bytes.AddRange(BitConverter.GetBytes(value))

[<Fact>]
let ``instructionByteLength is 1 for every no-operand instruction`` () =
    Assert.Equal(1, instructionByteLength Nil)
    Assert.Equal(1, instructionByteLength Pop)
    Assert.Equal(1, instructionByteLength Add)
    Assert.Equal(1, instructionByteLength Return)
    Assert.Equal(1, instructionByteLength Inherit)

[<Fact>]
let ``instructionByteLength is 3 for every single u16-operand instruction`` () =
    Assert.Equal(3, instructionByteLength (Constant 0))
    Assert.Equal(3, instructionByteLength (GetLocal 1))
    Assert.Equal(3, instructionByteLength (Jump 5))
    Assert.Equal(3, instructionByteLength (Call 2))

[<Fact>]
let ``instructionByteLength for Closure grows by 3 bytes per upvalue`` () =
    Assert.Equal(5, instructionByteLength (Closure(0, [])))
    Assert.Equal(8, instructionByteLength (Closure(0, [ { FromEnclosingLocal = true; Index = 0 } ])))

    Assert.Equal(
        11,
        instructionByteLength (
            Closure(0, [ { FromEnclosingLocal = true; Index = 0 }; { FromEnclosingLocal = false; Index = 1 } ])
        )
    )

[<Fact>]
let ``an empty chunk writes just the header and zero counts`` () =
    let bytes = writeAndRead { Constants = [||]; Code = [||] }

    let expected = ResizeArray<byte>()
    expected.AddRange("IQBC"B)
    expected.Add(FormatVersion)
    pushU32 expected 0u // constant_count
    pushU32 expected 0u // code_length

    Assert.Equal<byte[]>(expected.ToArray(), bytes)

[<Fact>]
let ``number and string constants encode with their tag, then payload`` () =
    let bytes =
        writeAndRead
            { Constants = [| NumberConstant 1.5; StringConstant "hi" |]
              Code = [| Constant 0; Constant 1; Return |] }

    let expected = ResizeArray<byte>()
    expected.AddRange("IQBC"B)
    expected.Add(FormatVersion)
    pushU32 expected 2u // constant_count
    expected.Add(0x00uy) // number tag
    expected.AddRange(BitConverter.GetBytes(1.5))
    expected.Add(0x01uy) // string tag
    pushU32 expected 2u
    expected.AddRange("hi"B)

    let code = ResizeArray<byte>()
    code.Add(0x01uy) // CONSTANT
    pushU16 code 0us
    code.Add(0x01uy) // CONSTANT
    pushU16 code 1us
    code.Add(0x23uy) // RETURN

    pushU32 expected (uint32 code.Count)
    expected.AddRange code

    Assert.Equal<byte[]>(expected.ToArray(), bytes)

[<Fact>]
let ``jump targets serialize as byte offsets, not instruction-array indices`` () =
    // index 0: Nil (offset 0, length 1); index 1: JumpIfFalse -> index 2
    // (offset 1, length 3); index 2: Pop (offset 4, length 1) -- so the
    // jump's target index (2) must serialize as byte offset 4, not 2.
    let bytes = writeAndRead { Constants = [||]; Code = [| Nil; JumpIfFalse 2; Pop |] }

    let code = ResizeArray<byte>()
    code.Add(0x02uy) // NIL
    code.Add(0x1Euy) // JUMP_IF_FALSE
    pushU16 code 4us
    code.Add(0x06uy) // POP

    let expected = ResizeArray<byte>()
    expected.AddRange("IQBC"B)
    expected.Add(FormatVersion)
    pushU32 expected 0u
    pushU32 expected (uint32 code.Count)
    expected.AddRange code

    Assert.Equal<byte[]>(expected.ToArray(), bytes)

[<Fact>]
let ``a jump targeting one past the end of the code uses the total code length`` () =
    // index 0: Jump -> index 1 (one past the end); index 1 doesn't exist,
    // so the target must be the total serialized byte length instead.
    let bytes = writeAndRead { Constants = [||]; Code = [| Jump 1 |] }

    let code = ResizeArray<byte>()
    code.Add(0x1Duy) // JUMP
    pushU16 code 3us // the whole (only) instruction is 3 bytes long

    let expected = ResizeArray<byte>()
    expected.AddRange("IQBC"B)
    expected.Add(FormatVersion)
    pushU32 expected 0u
    pushU32 expected (uint32 code.Count)
    expected.AddRange code

    Assert.Equal<byte[]>(expected.ToArray(), bytes)

[<Fact>]
let ``Closure encodes its function index, upvalue count, then each upvalue`` () =
    let bytes =
        writeAndRead
            { Constants = [| FunctionConstant { Name = "f"; Arity = 0; LocalCount = 0; Upvalues = []; Chunk = { Constants = [||]; Code = [||] } } |]
              Code = [| Closure(0, [ { FromEnclosingLocal = true; Index = 1 }; { FromEnclosingLocal = false; Index = 0 } ]) |] }

    let code = ResizeArray<byte>()
    code.Add(0x22uy) // CLOSURE
    pushU16 code 0us // function index
    pushU16 code 2us // upvalue count
    code.Add(1uy) // from enclosing local
    pushU16 code 1us
    code.Add(0uy) // from enclosing upvalue
    pushU16 code 0us

    // Only asserting the tail (the code section) here -- the constant
    // section's own FunctionConstant layout is covered separately below.
    let actualCode = bytes.[bytes.Length - code.Count ..]
    Assert.Equal<byte[]>(code.ToArray(), actualCode)

[<Fact>]
let ``a FunctionConstant serializes name, arity, local count, upvalues, then its nested chunk`` () =
    let proto =
        { Name = "f"
          Arity = 2
          LocalCount = 3
          Upvalues = [ { FromEnclosingLocal = true; Index = 0 } ]
          Chunk = { Constants = [||]; Code = [| Return |] } }

    let bytes = writeAndRead { Constants = [| FunctionConstant proto |]; Code = [||] }

    let expected = ResizeArray<byte>()
    expected.AddRange("IQBC"B)
    expected.Add(FormatVersion)
    pushU32 expected 1u // constant_count
    expected.Add(0x02uy) // function tag
    pushU32 expected 1u // name length
    expected.AddRange("f"B)
    expected.Add(2uy) // arity
    pushU16 expected 3us // local_count
    pushU16 expected 1us // upvalue_count
    expected.Add(1uy) // from enclosing local
    pushU16 expected 0us
    // nested chunk: 0 constants, then its own code
    pushU32 expected 0u
    pushU32 expected 1u // code_length (RETURN is 1 byte)
    expected.Add(0x23uy) // RETURN

    pushU32 expected 0u // top-level code_length

    Assert.Equal<byte[]>(expected.ToArray(), bytes)

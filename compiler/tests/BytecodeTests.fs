module BytecodeTests

open System
open System.IO
open Xunit
open Iqalox.Bytecode

let private pushU32 (bytes: ResizeArray<byte>) (value: uint32) =
    bytes.AddRange(BitConverter.GetBytes(value))

let private tempPath () =
    Path.Combine(Path.GetTempPath(), $"iqaloxc_test_{Guid.NewGuid()}.iqbc")

[<Fact>]
let ``an empty chunk writes just the header and zero counts`` () =
    let path = tempPath ()
    try
        write { Constants = []; Code = [||] } path

        let expected = ResizeArray<byte>()
        expected.AddRange("IQBC"B)
        expected.Add(0uy)
        pushU32 expected 0u // constant_count
        pushU32 expected 0u // code_length

        Assert.Equal<byte[]>(expected.ToArray(), File.ReadAllBytes(path))
    finally
        File.Delete(path)

[<Fact>]
let ``a chunk with one string constant and CONST_STRING/PRINT/HALT matches the v0 layout byte-for-byte`` () =
    let path = tempPath ()
    try
        let code = Array.concat [ constStringInstr 0u; printInstr; haltInstr ]
        write { Constants = [ "hello" ]; Code = code } path

        let expected = ResizeArray<byte>()
        expected.AddRange("IQBC"B)
        expected.Add(0uy)
        pushU32 expected 1u // constant_count
        expected.Add(0uy) // string tag
        pushU32 expected 5u // length
        expected.AddRange("hello"B)
        pushU32 expected (uint32 code.Length)
        expected.AddRange(code)

        Assert.Equal<byte[]>(expected.ToArray(), File.ReadAllBytes(path))
    finally
        File.Delete(path)

[<Fact>]
let ``constStringInstr encodes the opcode followed by a little-endian u32 index`` () =
    Assert.Equal<byte[]>([| 0x01uy; 0x2Auy; 0x00uy; 0x00uy; 0x00uy |], constStringInstr 42u)

[<Fact>]
let ``printInstr and haltInstr are single-byte opcodes`` () =
    Assert.Equal<byte[]>([| 0x02uy |], printInstr)
    Assert.Equal<byte[]>([| 0xFFuy |], haltInstr)

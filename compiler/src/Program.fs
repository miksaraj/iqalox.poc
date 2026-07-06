module Iqalox.Program

open Iqalox.Bytecode

// Phase 1 scope only (docs/PLAN-0.1.md): emits a fixed, hardcoded chunk
// rather than actually compiling a .iqx source file -- proving the
// compiler/vm round trip comes before the scanner/parser/codegen exist
// (Phases 2-5).
let helloWorldChunk =
    { Constants = [ "Hello from the Iqalox bytecode VM!" ]
      Code = Array.concat [ constStringInstr 0u; printInstr; haltInstr ] }

[<EntryPoint>]
let main argv =
    match argv with
    | [| outputPath |] ->
        write helloWorldChunk outputPath
        0
    | _ ->
        eprintfn "Usage: iqaloxc <output bytecode file>"
        64

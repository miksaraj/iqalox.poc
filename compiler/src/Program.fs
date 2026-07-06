/// The `iqaloxc` CLI: `.iqx` source -> bytecode file, running every Phase
/// 2-5 stage in sequence (scan, parse, resolve, generate code) and
/// stopping at the first stage that reports errors. Exit codes follow
/// `poc/src/iqalox.py`'s existing sysexits.h convention (64 usage, 65
/// source/compile error).
module Iqalox.Program

open System
open Iqalox.Scanner
open Iqalox.Parser
open Iqalox.Resolver
open Iqalox.Codegen
open Iqalox.Bytecode

[<EntryPoint>]
let main argv =
    match argv with
    | [| inputPath; outputPath |] ->
        let source = IO.File.ReadAllText inputPath
        let tokens, scanErrors = scanTokens source

        if not scanErrors.IsEmpty then
            for e in scanErrors do
                eprintfn "[line %d] Error: %s" e.Line e.Message
            65
        else
            let stmts, parseErrors = parse tokens

            if not parseErrors.IsEmpty then
                for e in parseErrors do
                    eprintfn "[line %d] Error: %s" e.Token.Line e.Message
                65
            else
                let bound, resolveErrors = resolve stmts

                if not resolveErrors.IsEmpty then
                    for e in resolveErrors do
                        eprintfn "[line %d] Error: %s" e.Token.Line e.Message
                    65
                else
                    let chunk, codegenErrors = compile bound

                    if not codegenErrors.IsEmpty then
                        for e in codegenErrors do
                            eprintfn "[line %d] Error: %s" e.Line e.Message
                        65
                    else
                        write chunk outputPath
                        0
    | _ ->
        eprintfn "Usage: iqaloxc <input .iqx file> <output bytecode file>"
        64

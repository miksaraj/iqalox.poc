/// The `iqaloxc` CLI: `.iqx` source -> bytecode file, running every Phase
/// 2-5 stage in sequence (scan, parse, resolve, generate code) and
/// stopping at the first stage that reports errors. Exit codes follow
/// `poc/src/iqalox.py`'s existing sysexits.h convention (64 usage, 65
/// source/compile error).
///
/// `docs/PLAN-0.2.md` Phase 5: `Prelude.source`'s `map`/`filter`/`reduce`/
/// `sort` are scanned and parsed here too, once per invocation, and their
/// statements are prepended to the user's own before resolving/compiling
/// -- one combined program, one `Chunk`, so `Resolver.fs`'s ordinary
/// global pre-registration sees both without any special-casing. Scanned
/// and parsed *separately* from the user's source purely so a failure in
/// each can be attributed to the right one -- an error inside
/// `Prelude.source` itself would mean a real bug in this compiler, not
/// anything the user wrote, so it's reported distinctly rather than
/// folded into the user's own error list.
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
        let preludeTokens, preludeScanErrors = scanTokens Prelude.source

        if not preludeScanErrors.IsEmpty then
            for e in preludeScanErrors do
                eprintfn "internal error: prelude failed to scan [line %d]: %s" e.Line e.Message
            70
        else
            let preludeStmts, preludeParseErrors = parse preludeTokens

            if not preludeParseErrors.IsEmpty then
                for e in preludeParseErrors do
                    eprintfn "internal error: prelude failed to parse [line %d]: %s" e.Token.Line e.Message
                70
            else
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
                        let bound, resolveErrors = resolve (preludeStmts @ stmts)

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

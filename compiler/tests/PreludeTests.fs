module PreludeTests

open Xunit
open Iqalox.Ast
open Iqalox.Scanner
open Iqalox.Parser
open Iqalox.Bound
open Iqalox.Resolver
open Iqalox.Codegen
open Iqalox.Prelude

/// Mirrors exactly what `Program.fs` does with `Prelude.source` before
/// resolving/compiling it together with a real user program -- these
/// tests exist because nothing else in `dotnet test` would otherwise
/// notice a syntax mistake in the embedded prelude text at all (it's a
/// plain string literal, invisible to the F# compiler).
let private preludeStmts () : Stmt list =
    let tokens, scanErrors = scanTokens source
    Assert.Empty scanErrors
    let stmts, parseErrors = parse tokens
    Assert.Empty parseErrors
    stmts

[<Fact>]
let ``the prelude scans and parses to exactly one FunctionStmt per stdlib function`` () =
    let stmts = preludeStmts ()
    let names =
        stmts
        |> List.map (function
            | FunctionStmt decl -> decl.Name.Lexeme
            | other -> failwith $"expected only FunctionStmt at the prelude's top level, got %A{other}")
    Assert.Equal<string list>([ "map"; "filter"; "reduce"; "sort"; "elementwise" ], names)

[<Fact>]
let ``the prelude resolves standalone with no errors`` () =
    let _, errors, _ = resolve (preludeStmts ())
    Assert.Empty errors

[<Fact>]
let ``the prelude compiles standalone with no errors`` () =
    let bound, resolveErrors, _ = resolve (preludeStmts ())
    Assert.Empty resolveErrors
    let _, codegenErrors = compile bound
    Assert.Empty codegenErrors

[<Fact>]
let ``a user program can call map/filter/reduce/sort/elementwise as ordinary globals once merged with the prelude`` () =
    // No wrapping parens around a multi-arg call's arguments (`f(a, b)` is
    // a single grouped comma-expression argument, not two arguments --
    // Argument()'s own doc comment) -- matches how the prelude itself
    // calls push/fn throughout.
    let userSource =
        "print map (x) -> x, [1]\nprint filter (x) -> x, [1]\nprint reduce (a, b) -> a, [1], 0\nprint sort (a, b) -> a, [1]\nprint elementwise (a, b) -> a, [[1]], [[1]]\n"
    let userTokens, userScanErrors = scanTokens userSource
    Assert.Empty userScanErrors
    let userStmts, userParseErrors = parse userTokens
    Assert.Empty userParseErrors

    let bound, errors, _ = resolve (preludeStmts () @ userStmts)
    Assert.Empty errors
    // The five prelude declarations resolve as globals, same as any
    // top-level `fun` -- and the user's own calls to them resolve as
    // ordinary GlobalBinding references, no different from calling
    // `push`/`length`/any other pre-existing global.
    let referencesGlobalCall (name: string) (stmt: BoundStmt) =
        match stmt with
        | BExpressionStmt(BCall(BVariable(_, printName), [ BCall(BVariable(GlobalBinding calleeName, _), _) ])) ->
            printName.Lexeme = "print" && calleeName = name
        | _ -> false
    match bound with
    | [ _; _; _; _; _; mapCall; filterCall; reduceCall; sortCall; elementwiseCall ] ->
        Assert.True(referencesGlobalCall "map" mapCall)
        Assert.True(referencesGlobalCall "filter" filterCall)
        Assert.True(referencesGlobalCall "reduce" reduceCall)
        Assert.True(referencesGlobalCall "sort" sortCall)
        Assert.True(referencesGlobalCall "elementwise" elementwiseCall)
    | other -> failwith $"unexpected shape: %A{other}"

[<Fact>]
let ``redeclaring map (or filter/reduce/sort) after the prelude is a compile-time already-declared error, matching push`` () =
    let source = "var map = 1\n"
    let tokens, _ = scanTokens source
    let stmts, _ = parse tokens
    let _, errors, _ = resolve (preludeStmts () @ stmts)
    Assert.Single errors |> ignore
    Assert.Contains("already declared", errors.[0].Message)

[<Fact>]
let ``push/pop/length/reverse/transpose/multiply/add/subtract are native globals, not part of the prelude's own FunctionStmt list`` () =
    // docs/PLAN-0.2.md Phase 5/6: these eight need direct ObjVector access
    // and never call back into user code, so they're true natives
    // (Resolver.fs's nativeGlobals), unlike map/filter/reduce/sort/
    // elementwise's prelude-source approach.
    let source =
        "push [], 1\npop [1]\nlength [1]\nreverse [1]\ntranspose [[1]]\nmultiply [[1]], [[1]]\nadd [[1]], [[1]]\nsubtract [[1]], [[1]]\n"
    let tokens, scanErrors = scanTokens source
    Assert.Empty scanErrors
    let stmts, parseErrors = parse tokens
    Assert.Empty parseErrors
    let _, errors, _ = resolve (preludeStmts () @ stmts)
    Assert.Empty errors

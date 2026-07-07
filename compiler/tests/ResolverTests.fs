module ResolverTests

open Xunit
open Iqalox.Ast
open Iqalox.Scanner
open Iqalox.Parser
open Iqalox.Bound
open Iqalox.Resolver

let private resolveSource (source: string) : BoundStmt list * ResolveError list =
    let source = if source.EndsWith "\n" then source else source + "\n"
    let tokens, _ = scanTokens source
    let stmts, _ = parse tokens
    resolve stmts

[<Fact>]
let ``a top-level var resolves as a global`` () =
    let bound, errors = resolveSource "var x = 1\nx"
    Assert.Empty errors
    match bound with
    | [ BVarStmt(DeclaredGlobal "x", _, _); BExpressionStmt(BVariable(GlobalBinding "x", _)) ] -> ()
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``a local var inside a block resolves to a stack slot`` () =
    let bound, errors = resolveSource "{ var x = 1\nx; }"
    Assert.Empty errors
    match bound with
    | [ BBlock [ BVarStmt(DeclaredLocal 0, _, _); BExpressionStmt(BVariable(LocalBinding 0, _)) ] ] -> ()
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``function parameters resolve as locals`` () =
    let bound, errors = resolveSource "fun f(a, b) { return a + b; }"
    Assert.Empty errors
    match bound with
    | [ BFunctionStmt(_, decl) ] ->
        match decl.Body with
        | [ BReturnStmt(_, Some(BBinary(BVariable(LocalBinding aSlot, _), _, BVariable(LocalBinding bSlot, _)))) ] ->
            Assert.Equal(0, aSlot)
            Assert.Equal(1, bSlot)
        | body -> failwith $"unexpected body: %A{body}"
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``a nested function capturing an enclosing local resolves as an upvalue`` () =
    let source = "fun outer() {\n    var x mut = 1\n    fun inner() { return x; }\n    return inner\n}"
    let bound, errors = resolveSource source
    Assert.Empty errors
    match bound with
    | [ BFunctionStmt(_, outerDecl) ] ->
        match outerDecl.Body with
        | [ BVarStmt(DeclaredLocal xSlot, _, _); BFunctionStmt(_, innerDecl); _ ] ->
            Assert.Equal<UpvalueDescriptor list>([ { FromEnclosingLocal = true; Index = xSlot } ], innerDecl.Upvalues)
            match innerDecl.Body with
            | [ BReturnStmt(_, Some(BVariable(UpvalueBinding 0, _))) ] -> ()
            | body -> failwith $"unexpected inner body: %A{body}"
        | body -> failwith $"unexpected outer body: %A{body}"
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``a doubly nested closure captures a grandparent local via an upvalue of an upvalue`` () =
    let source =
        "fun a() {\n"
        + "    var x mut = 1\n"
        + "    fun b() {\n"
        + "        fun c() { return x; }\n"
        + "        return c\n"
        + "    }\n"
        + "    return b\n"
        + "}"
    let bound, errors = resolveSource source
    Assert.Empty errors
    match bound with
    | [ BFunctionStmt(_, aDecl) ] ->
        match aDecl.Body with
        | [ BVarStmt(DeclaredLocal xSlot, _, _); BFunctionStmt(_, bDecl); _ ] ->
            // b captures x directly from a's locals.
            Assert.Equal<UpvalueDescriptor list>([ { FromEnclosingLocal = true; Index = xSlot } ], bDecl.Upvalues)
            match bDecl.Body with
            | [ BFunctionStmt(_, cDecl); _ ] ->
                // c captures x through b's own upvalue, not directly.
                Assert.Equal<UpvalueDescriptor list>([ { FromEnclosingLocal = false; Index = 0 } ], cDecl.Upvalues)
            | body -> failwith $"unexpected b body: %A{body}"
        | body -> failwith $"unexpected a body: %A{body}"
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``assigning to an immutable local is a compile error`` () =
    let _, errors = resolveSource "{ var x = 1\nx = 2; }"
    Assert.Single errors |> ignore
    Assert.Contains("immutable", errors.[0].Message)

[<Fact>]
let ``assigning to a mutable local has no error`` () =
    let _, errors = resolveSource "{ var x mut = 1\nx = 2; }"
    Assert.Empty errors

[<Fact>]
let ``assigning to an immutable global is a compile error regardless of declaration order`` () =
    // useX textually comes before x's own declaration -- the
    // pre-registration pass must still catch this.
    let _, errors = resolveSource "fun useX() { x = 2; }\nvar x = 1\n"
    Assert.Single errors |> ignore
    Assert.Contains("immutable", errors.[0].Message)

[<Fact>]
let ``redeclaring a variable in the same scope is an error`` () =
    let _, errors = resolveSource "{ var x = 1\nvar x = 2; }"
    Assert.Single errors |> ignore
    Assert.Contains("already declared", errors.[0].Message)

[<Fact>]
let ``shadowing in a nested scope is not an error`` () =
    let _, errors = resolveSource "{ var x = 1\n{ var x = 2\nx; } }"
    Assert.Empty errors

[<Fact>]
let ``redeclaring a global is an error`` () =
    let _, errors = resolveSource "var x = 1\nvar x = 2\n"
    Assert.Single errors |> ignore
    Assert.Contains("already declared", errors.[0].Message)

[<Fact>]
let ``a class can reference its own name from inside its own methods`` () =
    let bound, errors = resolveSource "class Duck {\n    quack() { return Duck; }\n}"
    Assert.Empty errors
    match bound with
    | [ BClassStmt(DeclaredGlobal "Duck", _, _, [ quackDecl ]) ] ->
        match quackDecl.Body with
        | [ BReturnStmt(_, Some(BVariable(GlobalBinding "Duck", _))) ] -> ()
        | body -> failwith $"unexpected method body: %A{body}"
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``self resolves inside a method`` () =
    let bound, errors = resolveSource "class Duck {\n    quack() { return self; }\n}"
    Assert.Empty errors
    match bound with
    | [ BClassStmt(_, _, _, [ quackDecl ]) ] ->
        match quackDecl.Body with
        | [ BReturnStmt(_, Some(BSelf(LocalBinding 0, _))) ] -> ()
        | body -> failwith $"unexpected method body: %A{body}"
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``self used outside a method is a compile error`` () =
    let _, errors = resolveSource "self"
    Assert.Single errors |> ignore
    Assert.Contains("outside of a method", errors.[0].Message)

[<Fact>]
let ``super resolves inside a method of a class with a superclass`` () =
    let source = "class A { greet() { return 1; } }\nclass B extends A {\n    greet() { return super.greet(); }\n}"
    let bound, errors = resolveSource source
    Assert.Empty errors
    match bound with
    | [ _; BClassStmt(_, _, Some(GlobalBinding "A", _), [ greetDecl ]) ] ->
        match greetDecl.Body with
        | [ BReturnStmt(_, Some(BCall(BSuper(LocalBinding 0, UpvalueBinding 0, _, methodName), []))) ] ->
            Assert.Equal("greet", methodName.Lexeme)
        | body -> failwith $"unexpected method body: %A{body}"
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``super used in a class without a superclass is a compile error`` () =
    let _, errors = resolveSource "class A {\n    greet() { return super.greet(); }\n}"
    Assert.Single errors |> ignore
    Assert.Contains("superclass", errors.[0].Message)

[<Fact>]
let ``super used outside any class is a compile error`` () =
    let _, errors = resolveSource "super.greet()"
    Assert.Single errors |> ignore
    Assert.Contains("superclass", errors.[0].Message)

[<Fact>]
let ``assigning to a function's own name is a compile error`` () =
    let _, errors = resolveSource "fun f() { }\nf = 1\n"
    Assert.Single errors |> ignore
    Assert.Contains("immutable", errors.[0].Message)

[<Fact>]
let ``assigning to a class's own name is a compile error`` () =
    let _, errors = resolveSource "class C { }\nC = 1\n"
    Assert.Single errors |> ignore
    Assert.Contains("immutable", errors.[0].Message)

[<Fact>]
let ``calling a native global like print needs no declaration and is not an error`` () =
    let bound, errors = resolveSource "print(1)"
    Assert.Empty errors
    match bound with
    | [ BExpressionStmt(BCall(BVariable(GlobalBinding "print", _), [ _ ])) ] -> ()
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``assigning to a native global like print is a compile error`` () =
    let _, errors = resolveSource "print = 1"
    Assert.Single errors |> ignore
    Assert.Contains("immutable", errors.[0].Message)

[<Fact>]
let ``redeclaring a native global like concat is a compile error`` () =
    let _, errors = resolveSource "var concat = 1"
    Assert.Single errors |> ignore
    Assert.Contains("already declared", errors.[0].Message)

[<Fact>]
let ``LocalCount includes self plus every declared local`` () =
    let bound, errors = resolveSource "class Duck {\n    quack(a) { var b = 1\nreturn a; }\n}"
    Assert.Empty errors
    match bound with
    | [ BClassStmt(_, _, _, [ quackDecl ]) ] -> Assert.Equal(3, quackDecl.LocalCount) // self, a, b
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``indexing resolves the object and index sub-expressions`` () =
    let bound, errors = resolveSource "var v = [1, 2]\nv[0]"
    Assert.Empty errors
    match bound with
    | [ BVarStmt(DeclaredGlobal "v", _, _)
        BExpressionStmt(BIndex(BVariable(GlobalBinding "v", _), BLiteral(NumberValue 0.0), _)) ] -> ()
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``indexed assignment resolves obj, index, and value sub-expressions`` () =
    let bound, errors = resolveSource "var v = [1, 2]\nv[0] = 9"
    Assert.Empty errors
    match bound with
    | [ BVarStmt(DeclaredGlobal "v", _, _)
        BExpressionStmt(BIndexSet(BVariable(GlobalBinding "v", _), BLiteral(NumberValue 0.0), BLiteral(NumberValue 9.0), _)) ] ->
        ()
    | _ -> failwith $"unexpected shape: %A{bound}"

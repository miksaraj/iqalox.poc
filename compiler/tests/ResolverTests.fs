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
    | [ BClassStmt(DeclaredGlobal "Duck", _, _, _, _, [ quackDecl ]) ] ->
        match quackDecl.Body with
        | [ BReturnStmt(_, Some(BVariable(GlobalBinding "Duck", _))) ] -> ()
        | body -> failwith $"unexpected method body: %A{body}"
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``self resolves inside a method`` () =
    let bound, errors = resolveSource "class Duck {\n    quack() { return self; }\n}"
    Assert.Empty errors
    match bound with
    | [ BClassStmt(_, _, _, _, _, [ quackDecl ]) ] ->
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
    | [ _; BClassStmt(_, _, Some(GlobalBinding "A", _), _, _, [ greetDecl ]) ] ->
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
    | [ BClassStmt(_, _, _, _, _, [ quackDecl ]) ] -> Assert.Equal(3, quackDecl.LocalCount) // self, a, b
    | _ -> failwith $"unexpected shape: %A{bound}"

// docs/PLAN-0.2.md Phase 7: property/method visibility (`pub`/`mut`).

[<Fact>]
let ``property declarations resolve with their pub/mut flags carried through`` () =
    let bound, errors = resolveSource "class Duck {\n    var name\n    var energy mut\n    var species pub\n    var quacking pub mut\n}"
    Assert.Empty errors
    match bound with
    | [ BClassStmt(_, _, _, _, [ name; energy; species; quacking ], []) ] ->
        Assert.Equal("name", name.Name.Lexeme)
        Assert.False(name.IsPub)
        Assert.False(name.IsMutable)
        Assert.False(energy.IsPub)
        Assert.True(energy.IsMutable)
        Assert.True(species.IsPub)
        Assert.False(species.IsMutable)
        Assert.True(quacking.IsPub)
        Assert.True(quacking.IsMutable)
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``a pub method's IsPub flag is carried into the bound method`` () =
    let bound, errors = resolveSource "class Duck {\n    pub quack() { return 1; }\n}"
    Assert.Empty errors
    match bound with
    | [ BClassStmt(_, _, _, _, _, [ quackDecl ]) ] -> Assert.True(quackDecl.IsPub)
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``self.x = value targeting an undeclared property is a compile-time error`` () =
    let _, errors = resolveSource "class Duck {\n    init() { self.name = \"x\"; }\n}"
    Assert.Single errors |> ignore
    Assert.Contains("not declared", errors.[0].Message)

[<Fact>]
let ``self.x = value targeting a property declared by an ancestor is not an error (protected-like)`` () =
    let source =
        "class Animal {\n    var name\n    init(n) { self.name = n; }\n}\nclass Dog extends Animal {\n    rename(n) { self.name = n; }\n}"
    let _, errors = resolveSource source
    Assert.Empty errors

[<Fact>]
let ``self.x = value targeting a method name is a distinct compile-time error`` () =
    let _, errors = resolveSource "class Duck {\n    quack() { return 1; }\n    init() { self.quack = 1; }\n}"
    Assert.Single errors |> ignore
    Assert.Contains("method, not a property", errors.[0].Message)

[<Fact>]
let ``a property and a method sharing the same name in one class is a compile-time error`` () =
    let _, errors = resolveSource "class Duck {\n    var quack\n    quack() { return 1; }\n}"
    Assert.Single errors |> ignore
    Assert.Contains("both a property and a method", errors.[0].Message)

[<Fact>]
let ``redeclaring the same property twice in one class is a compile-time error`` () =
    let _, errors = resolveSource "class Duck {\n    var name\n    var name mut\n}"
    Assert.Single errors |> ignore
    Assert.Contains("already declared", errors.[0].Message)

[<Fact>]
let ``redeclaring a property already declared by an ancestor is a compile-time error`` () =
    let source = "class Animal {\n    var name\n}\nclass Dog extends Animal {\n    var name mut\n}"
    let _, errors = resolveSource source
    Assert.Single errors |> ignore
    Assert.Contains("already declared by an ancestor", errors.[0].Message)

[<Fact>]
let ``external instance.x = value has no compile-time declared-property check`` () =
    // Unlike `self.x = value`, an external `instance.x = value` has no
    // statically-known class to check against -- deferred to a runtime
    // check instead (`vm/src/vm.cpp`'s SetProperty).
    let source = "class Duck {}\nvar d = Duck()\nd.name = 1"
    let _, errors = resolveSource source
    Assert.Empty errors

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

[<Fact>]
let ``a slice with both bounds resolves the object and both bound sub-expressions`` () =
    // docs/PLAN-0.3.md decision 3.
    let bound, errors = resolveSource "var v = [1, 2, 3]\nv[1:2]"
    Assert.Empty errors
    match bound with
    | [ BVarStmt(DeclaredGlobal "v", _, _)
        BExpressionStmt(BSlice(BVariable(GlobalBinding "v", _), Some(BLiteral(NumberValue 1.0)), Some(BLiteral(NumberValue 2.0)), _)) ] ->
        ()
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``a slice with an omitted bound resolves it as None, not a placeholder expression`` () =
    let bound, errors = resolveSource "var v = [1, 2, 3]\nv[:2]"
    Assert.Empty errors
    match bound with
    | [ BVarStmt(DeclaredGlobal "v", _, _)
        BExpressionStmt(BSlice(BVariable(GlobalBinding "v", _), None, Some(BLiteral(NumberValue 2.0)), _)) ] -> ()
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``a lambda's parameters resolve as locals, same as a named function's`` () =
    let bound, errors = resolveSource "(a, b) -> a + b"
    Assert.Empty errors
    match bound with
    | [ BExpressionStmt(BLambda decl) ] ->
        Assert.Equal(2, decl.LocalCount) // a, b
        match decl.Body with
        | [ BReturnStmt(_, Some(BBinary(BVariable(LocalBinding aSlot, _), _, BVariable(LocalBinding bSlot, _)))) ] ->
            Assert.Equal(0, aSlot)
            Assert.Equal(1, bSlot)
        | body -> failwith $"unexpected lambda body: %A{body}"
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``a lambda closes over an enclosing local exactly like a nested named function does`` () =
    let source = "fun outer() {\n    var x mut = 1\n    return (y) -> x + y\n}"
    let bound, errors = resolveSource source
    Assert.Empty errors
    match bound with
    | [ BFunctionStmt(_, outerDecl) ] ->
        match outerDecl.Body with
        | [ BVarStmt(DeclaredLocal xSlot, _, _); BReturnStmt(_, Some(BLambda lambdaDecl)) ] ->
            Assert.Equal<UpvalueDescriptor list>([ { FromEnclosingLocal = true; Index = xSlot } ], lambdaDecl.Upvalues)
            match lambdaDecl.Body with
            | [ BReturnStmt(_, Some(BBinary(BVariable(UpvalueBinding 0, _), _, BVariable(LocalBinding 0, _)))) ] -> ()
            | body -> failwith $"unexpected lambda body: %A{body}"
        | body -> failwith $"unexpected outer body: %A{body}"
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``a lambda parameter is immutable, matching a named function's parameters`` () =
    let _, errors = resolveSource "(x) -> x = 1"
    Assert.Single errors |> ignore
    Assert.Contains("immutable", errors.[0].Message)

[<Fact>]
let ``cons desugars to a call of a synthetic two-parameter closure`` () =
    let bound, errors = resolveSource "[1 | xs]"
    Assert.Empty errors
    match bound with
    | [ BExpressionStmt(BCall(BLambda decl, [ BLiteral(NumberValue 1.0); _ ])) ] ->
        Assert.Equal("cons", decl.Name.Lexeme)
        Assert.Equal(2, decl.Parameters.Length)
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``cons's arguments are resolved in the enclosing scope, not the synthetic closure's`` () =
    // `xs` is an enclosing global, evaluated once and passed as the
    // closure's second argument -- not looked up from inside the
    // synthetic closure's own body.
    let bound, errors = resolveSource "var xs = [2, 3]\n[1 | xs]"
    Assert.Empty errors
    match bound with
    | [ _; BExpressionStmt(BCall(BLambda _, [ _; BVariable(GlobalBinding "xs", _) ])) ] -> ()
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``list comprehension desugars to a call of a synthetic one-parameter closure`` () =
    let bound, errors = resolveSource "[x * 2 | x <- xs]"
    Assert.Empty errors
    match bound with
    | [ BExpressionStmt(BCall(BLambda decl, [ _ ])) ] ->
        Assert.Equal("comprehension", decl.Name.Lexeme)
        Assert.Equal(1, decl.Parameters.Length)
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``a list comprehension's body captures an enclosing local as an upvalue, exactly like a lambda`` () =
    let source = "fun outer() {\n    var factor mut = 10\n    return [x + factor | x <- [1, 2, 3]]\n}"
    let bound, errors = resolveSource source
    Assert.Empty errors
    match bound with
    | [ BFunctionStmt(_, outerDecl) ] ->
        match outerDecl.Body with
        | [ BVarStmt(DeclaredLocal factorSlot, _, _)
            BReturnStmt(_, Some(BCall(BLambda comprehensionDecl, [ _ ]))) ] ->
            Assert.Equal<UpvalueDescriptor list>(
                [ { FromEnclosingLocal = true; Index = factorSlot } ],
                comprehensionDecl.Upvalues
            )
        | body -> failwith $"unexpected outer body: %A{body}"
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``a list comprehension's bound variable shadows an outer variable of the same name without corrupting it`` () =
    let bound, errors = resolveSource "var n = 0\nvar shadow = [n | n <- [5, 6, 7]]\nn"
    Assert.Empty errors
    match bound with
    | [ BVarStmt(DeclaredGlobal "n", _, _); BVarStmt(DeclaredGlobal "shadow", _, _); BExpressionStmt(BVariable(GlobalBinding "n", _)) ] -> ()
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``a later generator may reference an earlier generator's bound name in its own source`` () =
    // docs/PLAN-0.3.md decision 1 -- resolved as a direct consequence of
    // the nested-loop desugaring, not a separately implemented feature.
    let bound, errors = resolveSource "[y | x <- [1, 2], y <- [x]]"
    Assert.Empty errors

[<Fact>]
let ``a guard clause resolves without error and still desugars to a one-parameter closure call`` () =
    let bound, errors = resolveSource "[x | x <- xs | x > 0]"
    Assert.Empty errors
    match bound with
    | [ BExpressionStmt(BCall(BLambda decl, [ _ ])) ] -> Assert.Equal(1, decl.Parameters.Length)
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``a spread element resolves to BSpread wrapping the resolved inner expression`` () =
    let bound, errors = resolveSource "var a = [1, 2]\n[...a]"
    Assert.Empty errors
    match bound with
    | [ BVarStmt(DeclaredGlobal "a", _, _); BExpressionStmt(BVector [ BSpread(BVariable(GlobalBinding "a", _), _) ]) ] -> ()
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``spread and plain elements resolve independently within the same vector`` () =
    let bound, errors = resolveSource "var a = [1]\n[0, ...a, 2]"
    Assert.Empty errors
    match bound with
    | [ _; BExpressionStmt(BVector [ BLiteral(NumberValue 0.0); BSpread(BVariable(GlobalBinding "a", _), _); BLiteral(NumberValue 2.0) ]) ] ->
        ()
    | other -> failwith $"unexpected shape: %A{other}"

[<Fact>]
let ``vector length and append are internal-only primitives usable directly`` () =
    let bound, errors = resolveSource "var v = [1, 2]\n[3 | v]"
    Assert.Empty errors
    match bound with
    | [ BVarStmt(DeclaredGlobal "v", _, _); BExpressionStmt(BCall(BLambda decl, _)) ] ->
        // The synthetic cons body's for-loop condition calls
        // InternalVectorLength on the closure's own $list parameter, and
        // its body calls InternalVectorAppend on $result.
        let rec containsInternalOps (stmts: BoundStmt list) =
            stmts
            |> List.exists (function
                | BForStmt(_, Some(BBinary(_, _, BVectorLengthInternal _)), _, body) -> containsInternalOps [ body ]
                | BExpressionStmt(BVectorAppendInternal _) -> true
                | BBlock inner -> containsInternalOps inner
                | _ -> false)
        Assert.True(containsInternalOps decl.Body)
    | _ -> failwith $"unexpected shape: %A{bound}"

// docs/PLAN-0.2.md Phase 8: mixins (`with`) and traits (`trait`/`use`).

[<Fact>]
let ``a used trait's method is inlined and resolves self relative to the using class`` () =
    let source = "trait Flyable {\n    pub fly() { return self; }\n}\nclass Duck {\n    use Flyable\n}"
    let bound, errors = resolveSource source
    Assert.Empty errors
    match bound with
    | [ BClassStmt(_, _, _, _, _, [ flyDecl ]) ] ->
        Assert.Equal("fly", flyDecl.Name.Lexeme)
        Assert.True(flyDecl.IsPub)
        match flyDecl.Body with
        | [ BReturnStmt(_, Some(BSelf(LocalBinding 0, _))) ] -> ()
        | body -> failwith $"unexpected inlined method body: %A{body}"
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``a trait declaration produces no BoundStmt of its own`` () =
    let bound, errors = resolveSource "trait Flyable {\n    pub fly() { return 1; }\n}\nvar x = 1"
    Assert.Empty errors
    match bound with
    | [ BVarStmt(DeclaredGlobal "x", _, _) ] -> ()
    | _ -> failwith $"expected the trait to vanish entirely, got %A{bound}"

[<Fact>]
let ``two used traits declaring the same method name is a compile-time error`` () =
    let source =
        "trait A {\n    pub go() { return 1; }\n}\ntrait B {\n    pub go() { return 2; }\n}\nclass C {\n    use A, B\n}"
    let _, errors = resolveSource source
    Assert.Single errors |> ignore
    Assert.Contains("declared by both", errors.[0].Message)

[<Fact>]
let ``a used trait conflicting with the class's own superclass is a compile-time error`` () =
    let source =
        "class Base {\n    pub go() { return 1; }\n}\ntrait Helper {\n    pub go() { return 2; }\n}\nclass C extends Base {\n    use Helper\n}"
    let _, errors = resolveSource source
    Assert.Single errors |> ignore
    Assert.Contains("declared by both", errors.[0].Message)

[<Fact>]
let ``the class's own method overriding a used trait's is not a conflict`` () =
    let source = "trait Flyable {\n    pub fly() { return 1; }\n}\nclass Duck {\n    use Flyable\n    pub fly() { return 2; }\n}"
    let bound, errors = resolveSource source
    Assert.Empty errors
    match bound with
    | [ BClassStmt(_, _, _, _, _, [ flyDecl ]) ] ->
        match flyDecl.Body with
        | [ BReturnStmt(_, Some(BLiteral(NumberValue 2.0))) ] -> ()
        | body -> failwith $"expected the class's own override to win, got %A{body}"
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``a circular trait use is a compile-time error, not an infinite loop`` () =
    let source = "trait A {\n    use B\n}\ntrait B {\n    use A\n}\nclass C {\n    use A\n}"
    let _, errors = resolveSource source
    Assert.NotEmpty errors
    Assert.Contains(errors, fun e -> e.Message.Contains "Circular trait composition")

[<Fact>]
let ``using an undefined trait is a compile-time error`` () =
    let _, errors = resolveSource "class Duck {\n    use Ghost\n}"
    Assert.Single errors |> ignore
    Assert.Contains("Undefined trait", errors.[0].Message)

[<Fact>]
let ``two with-mixins declaring the same member is a compile-time error`` () =
    let source =
        "class A {\n    pub go() { return 1; }\n}\nclass B {\n    pub go() { return 2; }\n}\nclass C with A, B {}"
    let _, errors = resolveSource source
    Assert.Single errors |> ignore
    Assert.Contains("declared by both", errors.[0].Message)

[<Fact>]
let ``a with-mixin resolves as an ordinary variable reference for the Mixin opcode`` () =
    let source = "class Named {\n    pub greet() { return 1; }\n}\nclass Robot with Named {}"
    let bound, errors = resolveSource source
    Assert.Empty errors
    match bound with
    | [ _; BClassStmt(_, _, None, [ (GlobalBinding "Named", mixinToken) ], [], []) ] -> Assert.Equal("Named", mixinToken.Lexeme)
    | _ -> failwith $"unexpected shape: %A{bound}"

[<Fact>]
let ``self.x = value targeting a property declared only by a with-mixin is not an error`` () =
    let source =
        "class Named {\n    var label\n    init(l) { self.label = l; }\n}\nclass Robot with Named {\n    rename(l) { self.label = l; }\n}"
    let _, errors = resolveSource source
    Assert.Empty errors

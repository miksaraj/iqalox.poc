module ParserTests

open Xunit
open Iqalox.Token
open Iqalox.Ast
open Iqalox.Scanner
open Iqalox.Parser

let private parseSource (source: string) : Stmt list =
    let source = if source.EndsWith "\n" then source else source + "\n"
    let tokens, _ = scanTokens source
    let stmts, _ = parse tokens
    stmts

let private parseWithErrors (source: string) : Stmt list * ParseError list =
    let source = if source.EndsWith "\n" then source else source + "\n"
    let tokens, _ = scanTokens source
    parse tokens

let private singleExpr (source: string) : Expr =
    match parseSource source with
    | [ ExpressionStmt expr ] -> expr
    | stmts -> failwith $"expected exactly one ExpressionStmt, got %A{stmts}"

[<Fact>]
let ``true, false, and nil are real literal values`` () =
    Assert.Equal(Literal(BoolValue false), singleExpr "false")
    Assert.Equal(Literal(BoolValue true), singleExpr "true")
    Assert.Equal(Literal NilValue, singleExpr "nil")

[<Fact>]
let ``logical or and and precedence`` () =
    match singleExpr "a and b or c and d" with
    | Logical(left, operator, right) ->
        Assert.Equal(Or, operator.Type)
        match left with
        | Logical(_, op, _) -> Assert.Equal(And, op.Type)
        | e -> failwith $"expected Logical, got %A{e}"
        match right with
        | Logical(_, op, _) -> Assert.Equal(And, op.Type)
        | e -> failwith $"expected Logical, got %A{e}"
    | e -> failwith $"expected Logical, got %A{e}"

[<Fact>]
let ``null-coalescing binds looser than logical and tighter than ternary`` () =
    // `??` sits between `logic_or` and `ternary`: logical operators group
    // first (`a ?? (b or c)`), and `??` itself groups before the ternary
    // gets to see it (`(a ?? b) ? c : d`).
    match singleExpr "a ?? b or c" with
    | Binary(_, operator, right) ->
        Assert.Equal(DoubleQuestionMark, operator.Type)
        match right with
        | Logical(_, op, _) -> Assert.Equal(Or, op.Type)
        | e -> failwith $"expected Logical, got %A{e}"
    | e -> failwith $"expected Binary, got %A{e}"

    match singleExpr "a ?? b ? c : d" with
    | Ternary(left, _, _, _, _) ->
        match left with
        | Binary(_, op, _) -> Assert.Equal(DoubleQuestionMark, op.Type)
        | e -> failwith $"expected Binary, got %A{e}"
    | e -> failwith $"expected Ternary, got %A{e}"

[<Fact>]
let ``logical sits between ternary and equality`` () =
    // `a == b and c == d` should group as `(a == b) and (c == d)`, and the
    // whole thing should still be usable as a ternary condition without
    // needing its own wrapping parens.
    match singleExpr "a == b and c == d ? 1 : 2" with
    | Ternary(left, _, _, _, _) ->
        match left with
        | Logical _ -> ()
        | e -> failwith $"expected Logical, got %A{e}"
    | e -> failwith $"expected Ternary, got %A{e}"

[<Fact>]
let ``break and continue are expressions`` () =
    match singleExpr "break" with
    | BreakExpr _ -> ()
    | e -> failwith $"expected BreakExpr, got %A{e}"
    match singleExpr "continue" with
    | ContinueExpr _ -> ()
    | e -> failwith $"expected ContinueExpr, got %A{e}"

[<Fact>]
let ``break and continue are usable as ternary branches`` () =
    match singleExpr "a ? continue : b ? break : c" with
    | Ternary(_, _, middle, _, right) ->
        match middle with
        | ContinueExpr _ -> ()
        | e -> failwith $"expected ContinueExpr, got %A{e}"
        match right with
        | Ternary(_, _, innerMiddle, _, _) ->
            match innerMiddle with
            | BreakExpr _ -> ()
            | e -> failwith $"expected BreakExpr, got %A{e}"
        | e -> failwith $"expected Ternary, got %A{e}"
    | e -> failwith $"expected Ternary, got %A{e}"

[<Fact>]
let ``for statement parses all clauses`` () =
    match parseSource "for (var i mut = 0; i < 5; ++i) { print i; }" with
    | [ ForStmt(initializer, condition, increment, _) ] ->
        Assert.True(initializer.IsSome)
        Assert.True(condition.IsSome)
        Assert.True(increment.IsSome)
    | stmts -> failwith $"expected one ForStmt, got %A{stmts}"

[<Fact>]
let ``for statement clauses are optional`` () =
    match parseSource "for (;;) { break; }" with
    | [ ForStmt(initializer, condition, increment, _) ] ->
        Assert.True(initializer.IsNone)
        Assert.True(condition.IsNone)
        Assert.True(increment.IsNone)
    | stmts -> failwith $"expected one ForStmt, got %A{stmts}"

[<Fact>]
let ``a for loop with an empty statement body does not crash`` () =
    // Bug fix vs. poc: poc's for_statement() assigns `body = self.statement()`
    // with no None-check, but `statement()` returns None for a bare `;`
    // body -- constructing a For node with a None body, which crashes the
    // interpreter later (`AttributeError` on `None.accept`). Treated here
    // as an empty block instead.
    match parseSource "for (;;) ;" with
    | [ ForStmt(_, _, _, Block []) ] -> ()
    | stmts -> failwith $"expected a ForStmt with an empty Block body, got %A{stmts}"

[<Fact>]
let ``increment requires an assignable target`` () =
    // `++5` isn't a variable, so it shouldn't parse as an increment.
    Assert.Empty(parseSource "++5")

[<Fact>]
let ``increment on a variable parses`` () =
    match singleExpr "++x" with
    | Unary(operator, right) ->
        Assert.Equal(PlusPlus, operator.Type)
        match right with
        | Variable _ -> ()
        | e -> failwith $"expected Variable, got %A{e}"
    | e -> failwith $"expected Unary, got %A{e}"

[<Fact>]
let ``mut declaration parses`` () =
    match parseSource "var x mut = 1" with
    | [ VarStmt(_, _, isMutable) ] -> Assert.True isMutable
    | stmts -> failwith $"expected one VarStmt, got %A{stmts}"

[<Fact>]
let ``immutable declaration parses`` () =
    match parseSource "var x = 1" with
    | [ VarStmt(_, _, isMutable) ] -> Assert.False isMutable
    | stmts -> failwith $"expected one VarStmt, got %A{stmts}"

[<Fact>]
let ``blank lines between statements do not error`` () =
    Assert.Equal(2, (parseSource "var x = 1\n\n\nprint x").Length)

[<Fact>]
let ``function declaration parses params and body`` () =
    match parseSource "fun add(a, b) { return a + b; }" with
    | [ FunctionStmt decl ] ->
        Assert.Equal("add", decl.Name.Lexeme)
        Assert.Equal<string list>([ "a"; "b" ], decl.Parameters |> List.map (fun p -> p.Lexeme))
        Assert.Single(decl.Body) |> ignore
        match decl.Body.[0] with
        | ReturnStmt _ -> ()
        | s -> failwith $"expected ReturnStmt, got %A{s}"
    | stmts -> failwith $"expected one FunctionStmt, got %A{stmts}"

[<Fact>]
let ``zero-arg call requires empty parens`` () =
    match singleExpr "f()" with
    | Call(Variable name, arguments) ->
        Assert.Equal("f", name.Lexeme)
        Assert.Empty(arguments)
    | e -> failwith $"expected Call, got %A{e}"

[<Fact>]
let ``a bare identifier is not a call`` () =
    // No `()` and nothing recognizable as an argument follows -- `f` alone
    // is a value reference, not an invocation (needed so functions can be
    // passed around without being called, e.g. `return adder`).
    match singleExpr "f" with
    | Variable _ -> ()
    | e -> failwith $"expected Variable, got %A{e}"

[<Fact>]
let ``single-arg call needs no parens`` () =
    match singleExpr "f 1" with
    | Call(_, [ Literal(NumberValue 1.0) ]) -> ()
    | e -> failwith $"expected Call with one NumberValue 1.0 argument, got %A{e}"

[<Fact>]
let ``multi-arg call is comma-separated without wrapping parens`` () =
    match singleExpr "f 1, 2, 3" with
    | Call(_, arguments) ->
        let values =
            arguments
            |> List.map (function
                | Literal(NumberValue n) -> n
                | e -> failwith $"expected NumberValue, got %A{e}")
        Assert.Equal<float list>([ 1.0; 2.0; 3.0 ], values)
    | e -> failwith $"expected Call, got %A{e}"

[<Fact>]
let ``a compound argument needs grouping parens`` () =
    // `f - 1` is subtraction (f minus 1), never a call to f with a bare
    // unary/binary expression as its argument.
    match singleExpr "f - 1" with
    | Binary(_, operator, _) -> Assert.Equal(Minus, operator.Type)
    | e -> failwith $"expected Binary, got %A{e}"

    match singleExpr "f (n - 1)" with
    | Call(_, [ Grouping _ ]) -> ()
    | e -> failwith $"expected Call with one Grouping argument, got %A{e}"

[<Fact>]
let ``a nested call needs no extra parens`` () =
    // `concat` immediately followed by a vector literal is itself a
    // complete call, which then becomes print's one argument.
    match singleExpr "print concat [1, 2]" with
    | Call(Variable printName, [ Call(Variable concatName, [ Vector _ ]) ]) ->
        Assert.Equal("print", printName.Lexeme)
        Assert.Equal("concat", concatName.Lexeme)
    | e -> failwith $"expected nested Call, got %A{e}"

[<Fact>]
let ``vector literal with multiple elements`` () =
    match singleExpr "[1, 2, 3]" with
    | Vector values ->
        let numbers =
            values
            |> List.map (function
                | Literal(NumberValue n) -> n
                | e -> failwith $"expected NumberValue, got %A{e}")
        Assert.Equal<float list>([ 1.0; 2.0; 3.0 ], numbers)
    | e -> failwith $"expected Vector, got %A{e}"

[<Fact>]
let ``empty and single-element vector literals`` () =
    Assert.Equal(Vector [], singleExpr "[]")
    Assert.Equal(Vector [ Literal(NumberValue 1.0) ], singleExpr "[1]")

[<Fact>]
let ``a vector literal parse error does not leave the comma operator disabled`` () =
    // Bug fix vs. poc: poc's primary() restores comma_as_operator with a
    // plain assignment *after* parsing a vector literal's elements, so an
    // error partway through (here, a bare '+' isn't a valid element) skips
    // that restore -- since the exception unwinds straight past it --
    // leaving the comma operator permanently disabled for the rest of the
    // file. The next line's `a, b` would then fail to parse too.
    let stmts, _ = parseWithErrors "[1, 2, +]\na, b\n"
    match stmts with
    | [ ExpressionStmt(Binary(Variable a, operator, Variable b)) ] ->
        Assert.Equal(Comma, operator.Type)
        Assert.Equal("a", a.Lexeme)
        Assert.Equal("b", b.Lexeme)
    | _ -> failwith $"expected the second line to parse as one comma-expression statement, got %A{stmts}"

[<Fact>]
let ``pipe desugars to a call`` () =
    match singleExpr "a |> f" with
    | Call(Variable f, [ Variable a ]) ->
        Assert.Equal("f", f.Lexeme)
        Assert.Equal("a", a.Lexeme)
    | e -> failwith $"expected Call, got %A{e}"

[<Fact>]
let ``pipe chains left-associatively`` () =
    // `a |> f |> g` is `g(f(a))`.
    match singleExpr "a |> f |> g" with
    | Call(Variable g, [ Call(Variable f, [ Variable a ]) ]) ->
        Assert.Equal("g", g.Lexeme)
        Assert.Equal("f", f.Lexeme)
        Assert.Equal("a", a.Lexeme)
    | e -> failwith $"expected nested Call, got %A{e}"

[<Fact>]
let ``pipe requires a function reference on the right`` () =
    // `a |> 1` doesn't make sense -- the right side must be a bare name.
    Assert.Empty(parseSource "a |> 1")

[<Fact>]
let ``pipe binds looser than comma and ternary`` () =
    // A ternary as the whole pipe input needs no extra grouping.
    match singleExpr "(a ? b : c) |> f" with
    | Call(_, [ Grouping(Ternary _) ]) -> ()
    | e -> failwith $"expected Call with a grouped Ternary argument, got %A{e}"

[<Fact>]
let ``the ignore operator is an expression`` () =
    Assert.Equal(Ignore, singleExpr "_")

[<Fact>]
let ``the ignore operator is usable as a ternary branch`` () =
    match singleExpr "a ? _ : b" with
    | Ternary(_, _, middle, _, _) -> Assert.Equal(Ignore, middle)
    | e -> failwith $"expected Ternary, got %A{e}"

[<Fact>]
let ``class declaration parses methods`` () =
    match parseSource "class Duck { quack() { return 1; } }" with
    | [ ClassStmt(name, superclass, [ method_ ]) ] ->
        Assert.Equal("Duck", name.Lexeme)
        Assert.True(superclass.IsNone)
        Assert.Equal("quack", method_.Name.Lexeme)
    | stmts -> failwith $"expected one ClassStmt with one method, got %A{stmts}"

[<Fact>]
let ``class declaration parses a superclass`` () =
    match parseSource "class B extends A {}" with
    | [ ClassStmt(_, Some(Variable superName), _) ] -> Assert.Equal("A", superName.Lexeme)
    | stmts -> failwith $"expected one ClassStmt with a Variable superclass, got %A{stmts}"

[<Fact>]
let ``class body tolerates blank lines`` () =
    match parseSource "class Duck {\n\n    quack() { return 1; }\n\n}" with
    | [ ClassStmt(_, _, [ _ ]) ] -> ()
    | stmts -> failwith $"expected one ClassStmt with one method, got %A{stmts}"

[<Fact>]
let ``property access is a Get expression`` () =
    match singleExpr "duck.name" with
    | Get(Variable objName, name) ->
        Assert.Equal("name", name.Lexeme)
        Assert.Equal("duck", objName.Lexeme)
    | e -> failwith $"expected Get, got %A{e}"

[<Fact>]
let ``property call with zero args`` () =
    match singleExpr "duck.quack()" with
    | Call(Get(_, name), []) -> Assert.Equal("quack", name.Lexeme)
    | e -> failwith $"expected zero-arg Call on a Get, got %A{e}"

[<Fact>]
let ``property call with args needs no parens`` () =
    match singleExpr "math.square 3" with
    | Call(Get(_, name), [ Literal(NumberValue 3.0) ]) -> Assert.Equal("square", name.Lexeme)
    | e -> failwith $"expected Call on a Get, got %A{e}"

[<Fact>]
let ``property assignment is a Set expression`` () =
    match singleExpr "self.name = value" with
    | Set(SelfExpr _, name, Variable value) ->
        Assert.Equal("name", name.Lexeme)
        Assert.Equal("value", value.Lexeme)
    | e -> failwith $"expected Set, got %A{e}"

[<Fact>]
let ``super method call`` () =
    match singleExpr "super.test()" with
    | Call(SuperExpr(_, method_), []) -> Assert.Equal("test", method_.Lexeme)
    | e -> failwith $"expected zero-arg Call on a SuperExpr, got %A{e}"

[<Fact>]
let ``self is an expression`` () =
    match singleExpr "self" with
    | SelfExpr _ -> ()
    | e -> failwith $"expected SelfExpr, got %A{e}"

[<Fact>]
let ``no-space bracket after an identifier is indexing`` () =
    match singleExpr "v[0]" with
    | Index(Variable objName, Literal(NumberValue 0.0), _) -> Assert.Equal("v", objName.Lexeme)
    | e -> failwith $"expected Index, got %A{e}"

[<Fact>]
let ``space before bracket after an identifier is still a vector-literal call argument`` () =
    // docs/PLAN-0.2.md decision 6's whitespace-adjacency resolution: this
    // must keep meaning exactly what it meant before indexing existed.
    match singleExpr "concat [1, 2]" with
    | Call(Variable name, [ Vector [ Literal(NumberValue 1.0); Literal(NumberValue 2.0) ] ]) ->
        Assert.Equal("concat", name.Lexeme)
    | e -> failwith $"expected Call with a Vector argument, got %A{e}"

[<Fact>]
let ``chained indexing`` () =
    match singleExpr "grid[0][1]" with
    | Index(Index(Variable objName, Literal(NumberValue 0.0), _), Literal(NumberValue 1.0), _) ->
        Assert.Equal("grid", objName.Lexeme)
    | e -> failwith $"expected nested Index, got %A{e}"

[<Fact>]
let ``indexing a zero-arg call result needs no extra parens`` () =
    match singleExpr "makeVector()[0]" with
    | Index(Call(Variable name, []), Literal(NumberValue 0.0), _) -> Assert.Equal("makeVector", name.Lexeme)
    | e -> failwith $"expected Index over a Call, got %A{e}"

[<Fact>]
let ``no-space bracket after a property access is indexing, not a call`` () =
    match singleExpr "obj.method[0]" with
    | Index(Get(_, name), Literal(NumberValue 0.0), _) -> Assert.Equal("method", name.Lexeme)
    | e -> failwith $"expected Index over a Get, got %A{e}"

[<Fact>]
let ``space before bracket after a property access is still a vector-literal call argument`` () =
    match singleExpr "obj.method [0]" with
    | Call(Get(_, name), [ Vector [ Literal(NumberValue 0.0) ] ]) -> Assert.Equal("method", name.Lexeme)
    | e -> failwith $"expected Call with a Vector argument, got %A{e}"

[<Fact>]
let ``indexing is a valid assignment target`` () =
    match singleExpr "v[0] = 1" with
    | IndexSet(Variable objName, Literal(NumberValue 0.0), Literal(NumberValue 1.0), _) ->
        Assert.Equal("v", objName.Lexeme)
    | e -> failwith $"expected IndexSet, got %A{e}"

[<Fact>]
let ``a single-parameter lambda parses as a Lambda expression`` () =
    match singleExpr "(n) -> n * n" with
    | Lambda([ param ], _, Binary(Variable left, _, Variable right)) ->
        Assert.Equal("n", param.Lexeme)
        Assert.Equal("n", left.Lexeme)
        Assert.Equal("n", right.Lexeme)
    | e -> failwith $"expected Lambda, got %A{e}"

[<Fact>]
let ``a multi-parameter lambda parses both parameter names`` () =
    match singleExpr "(a, b) -> a + b" with
    | Lambda([ a; b ], _, _) ->
        Assert.Equal("a", a.Lexeme)
        Assert.Equal("b", b.Lexeme)
    | e -> failwith $"expected Lambda, got %A{e}"

[<Fact>]
let ``a zero-parameter lambda parses with an empty parameter list`` () =
    match singleExpr "() -> 1" with
    | Lambda([], _, Literal(NumberValue 1.0)) -> ()
    | e -> failwith $"expected zero-parameter Lambda, got %A{e}"

[<Fact>]
let ``a grouped comma expression with no arrow after it is not a lambda`` () =
    // docs/PLAN-0.2.md decision 1's lookahead-past-')' disambiguation:
    // (a, b) alone is still 0.1's comma operator wrapped in a Grouping.
    match singleExpr "(a, b)" with
    | Grouping(Binary(Variable a, operator, Variable b)) ->
        Assert.Equal(Comma, operator.Type)
        Assert.Equal("a", a.Lexeme)
        Assert.Equal("b", b.Lexeme)
    | e -> failwith $"expected a Grouping around a comma Binary, got %A{e}"

[<Fact>]
let ``a lambda is a valid call argument with no extra wrapping parens`` () =
    match singleExpr "applyTwice square, (x) -> x + 1" with
    | Call(Variable name, [ Variable squareName; Lambda([ param ], _, _) ]) ->
        Assert.Equal("applyTwice", name.Lexeme)
        Assert.Equal("square", squareName.Lexeme)
        Assert.Equal("x", param.Lexeme)
    | e -> failwith $"expected Call with a Lambda argument, got %A{e}"

[<Fact>]
let ``a curried lambda's body is itself a lambda`` () =
    match singleExpr "(x) -> (y) -> x + y" with
    | Lambda([ x ], _, Lambda([ y ], _, _)) ->
        Assert.Equal("x", x.Lexeme)
        Assert.Equal("y", y.Lexeme)
    | e -> failwith $"expected a Lambda whose body is another Lambda, got %A{e}"

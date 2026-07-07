/// Parser: `Token` list -> `Stmt` list. Recursive-descent, porting
/// `poc/src/parser.py`'s grammar (the paren-free call syntax, the full
/// ternary/logical/comparison precedence chain, pipe, comma, classes,
/// vector literals, `self`/`super`) -- see `langspec/SYNTAX_GRAMMAR.md`
/// for the authoritative grammar this implements.
///
/// Errors are collected into a list rather than reported through a global
/// mutable singleton the way `poc`'s `Iqalox.error()` works -- matching
/// `Scanner.fs`'s `ScanError` pattern, this keeps "produce an AST" and
/// "how errors get displayed" as separate concerns. Recovery still works
/// the same way `poc`'s does: a parse failure raises (unwinding whatever
/// nested grammar rule was mid-parse), caught at `Declaration()`, which
/// then skips forward to the next likely statement boundary
/// (`Synchronize()`) and continues -- one bad statement doesn't abort the
/// whole file.
///
/// Not a line-for-line port in one respect: `primary()`'s vector-literal
/// handling toggles `comma_as_operator` off, parses the elements, then
/// toggles it back on -- but `poc` does this without a `try`/`finally`
/// equivalent, so a parse error partway through a vector literal's
/// elements (e.g. `[1, 2, bad+]`) leaves the comma operator permanently
/// disabled for the rest of that parse, since the restoring assignment
/// never runs. Fixed here with a `try`/`finally` so the flag is always
/// restored, matching what was clearly intended.
///
/// `0.2` (`docs/PLAN-0.2.md` Phase 1) adds postfix indexing (`v[0]`,
/// `Ast.Index`/`Ast.IndexSet`) to `Call()`'s existing `.`-chaining loop.
/// This collides, at the token-stream level, with the pre-existing "bare
/// identifier/property + vector-literal argument" call syntax (`concat
/// [1, 2]`) -- `v[0]` and `v` called with a one-element vector argument
/// `[0]` are otherwise indistinguishable. Resolved by whitespace
/// adjacency (`isAdjacent`/`startsArgumentAfter`): a `[` with no space
/// before it is always postfix indexing; a `[` with a space is always the
/// existing vector-literal-argument call, unchanged. A real design
/// question the original `0.2` grammar draft missed entirely -- see
/// `docs/PLAN-0.2.md` decision 6's addendum.
module Iqalox.Parser

open Iqalox.Token
open Iqalox.Ast

type ParseError = { Message: string; Token: Token }

exception ParseFailure of ParseError

/// Tokens that can start a call argument -- deliberately excludes
/// operator tokens like MINUS: `f - 1` is subtraction, never a call to
/// `f` with argument `-1` (that needs `f (-1)`) -- this is what keeps
/// `x - 1` and `f 1` unambiguous without lookahead past one token.
let private argumentStartTokens =
    Set.ofList [
        LeftParen; LeftBracket; False; True; Nil; Number; String; Identifier; Break; Continue; Underscore; Self; Super
    ]

/// True when `right` immediately follows `left` with no whitespace
/// between them on the same line -- e.g. `v` and `[` in `v[0]`. Tells
/// postfix indexing (`v[0]`, no space) apart from the pre-existing "bare
/// identifier/property + vector-literal argument" call syntax
/// (`concat [1, 2]`, always space-separated in practice): both parse to
/// the identical token stream, so this adjacency check is the only
/// tiebreaker (docs/PLAN-0.2.md decision 6's whitespace-adjacency
/// resolution -- a real grammar collision the original `0.2` grammar
/// draft missed).
let private isAdjacent (left: Token) (right: Token) =
    left.Line = right.Line && right.Column = left.Column + left.Lexeme.Length

/// Like `startsArgument`, but a `[` immediately adjacent to `prev` is
/// reserved for postfix indexing, never a vector-literal call argument --
/// the only argument-start token that collides with a postfix operator
/// this way (see `isAdjacent`).
let private startsArgumentAfter (prev: Token) (token: Token) =
    if token.Type = LeftBracket && isAdjacent prev token then
        false
    else
        Set.contains token.Type argumentStartTokens

let private literalValueOf (token: Token) : LiteralValue =
    match token.Literal with
    | NumberLiteral n -> NumberValue n
    | StringLiteral s -> StringValue s
    | NoLiteral -> failwith "unreachable: a NUMBER/STRING token always carries a literal value"

type private ParserState(tokens: Token[]) =
    let errors = ResizeArray<ParseError>()
    let mutable current = 0
    let mutable commaAsOperator = true

    let isAtEnd () = tokens.[current].Type = Eof
    let peek () = tokens.[current]

    let peekAt (offset: int) =
        let index = min (current + offset) (tokens.Length - 1)
        tokens.[index]

    let previous () = tokens.[current - 1]

    let advance () =
        if not (isAtEnd ()) then
            current <- current + 1
        previous ()

    let check (tokenType: TokenType) = not (isAtEnd ()) && (peek ()).Type = tokenType
    let checkAt (offset: int) (tokenType: TokenType) = (peekAt offset).Type = tokenType

    /// True when the `(` at the current position starts a lambda
    /// parameter list (`(a, b) -> expr`, decision 1) rather than a
    /// grouped/comma expression -- `0.1`'s comma operator already makes
    /// `(a, b)` a valid expression on its own (evaluate `a`, discard it,
    /// yield `b`) using the exact same opening token, so the two can only
    /// be told apart by scanning ahead (without consuming anything) for a
    /// bare, comma-separated identifier list immediately followed by
    /// `) ->`. `peekAt 0` is the `(` itself.
    let isLambdaAhead () =
        let mutable i = 1
        let mutable ok = true
        if not (checkAt i RightParen) then
            let mutable scanning = true
            while scanning do
                if checkAt i Identifier then
                    i <- i + 1
                    if checkAt i Comma then
                        i <- i + 1
                    else
                        scanning <- false
                else
                    ok <- false
                    scanning <- false
            if ok && not (checkAt i RightParen) then
                ok <- false
        if ok then
            i <- i + 1
        ok && checkAt i Arrow

    let matchAny (types: TokenType list) =
        if types |> List.exists check then
            advance () |> ignore
            true
        else
            false

    let error (token: Token) (message: string) : 'a =
        errors.Add { Message = message; Token = token }
        raise (ParseFailure { Message = message; Token = token })

    let consume (tokenType: TokenType) (message: string) : Token =
        if check tokenType then advance () else error (peek ()) message

    member this.Parse() : Stmt list * ParseError list =
        let statements = ResizeArray<Stmt>()
        while not (isAtEnd ()) do
            match this.Declaration() with
            | Some stmt -> statements.Add stmt
            | None -> ()
        List.ofSeq statements, List.ofSeq errors

    member this.Expression() : Expr = this.Assignment()

    member this.Declaration() : Stmt option =
        try
            if matchAny [ Class ] then Some(this.ClassDeclaration())
            elif matchAny [ Fun ] then Some(FunctionStmt(this.FunctionDeclaration "function"))
            elif matchAny [ Var ] then Some(this.VarDeclaration())
            else this.Statement()
        with ParseFailure _ ->
            this.Synchronize()
            None

    member this.ClassDeclaration() : Stmt =
        let name = consume Identifier "Expect class name."

        let superclass =
            if matchAny [ Extends ] then
                consume Identifier "Expect superclass name." |> ignore
                Some(Variable(previous ()))
            else
                None

        consume LeftBrace "Expect '{' before class body." |> ignore

        let methods = ResizeArray<FunctionDecl>()
        while not (check RightBrace) && not (isAtEnd ()) do
            if matchAny [ Semicolon ] then
                ()
            else
                methods.Add(this.FunctionDeclaration "method")

        consume RightBrace "Expect '}' after class body." |> ignore
        ClassStmt(name, superclass, List.ofSeq methods)

    member this.FunctionDeclaration(kind: string) : FunctionDecl =
        let name = consume Identifier $"Expect {kind} name."
        consume LeftParen $"Expect '(' after {kind} name." |> ignore

        let parameters = ResizeArray<Token>()
        if not (check RightParen) then
            let mutable more = true
            while more do
                parameters.Add(consume Identifier "Expect parameter name.")
                more <- matchAny [ Comma ]

        consume RightParen "Expect ')' after parameters." |> ignore
        consume LeftBrace $"Expect '{{' before {kind} body." |> ignore
        let body = this.Block()

        { Name = name; Parameters = List.ofSeq parameters; Body = body }

    member this.Statement() : Stmt option =
        if matchAny [ Semicolon ] then None
        elif matchAny [ For ] then Some(this.ForStatement())
        elif matchAny [ Return ] then Some(this.ReturnStatement())
        elif matchAny [ LeftBrace ] then Some(Block(this.Block()))
        else this.ExpressionStatement()

    member this.ReturnStatement() : Stmt =
        let keyword = previous ()
        let value = if check Semicolon then None else Some(this.Expression())
        consume Semicolon "Expect line break or ';' after return value." |> ignore
        ReturnStmt(keyword, value)

    member this.ForStatement() : Stmt =
        consume LeftParen "Expect '(' after 'for'." |> ignore

        let initializer =
            if matchAny [ Semicolon ] then None
            elif matchAny [ Var ] then Some(this.VarDeclaration())
            else this.ExpressionStatement()

        let condition = if check Semicolon then None else Some(this.Expression())
        consume Semicolon "Expect ';' after loop condition." |> ignore

        let increment = if check RightParen then None else Some(this.Expression())
        consume RightParen "Expect ')' after for clauses." |> ignore

        let body =
            match this.Statement() with
            | Some stmt -> stmt
            | None -> Block []

        ForStmt(initializer, condition, increment, body)

    member this.VarDeclaration() : Stmt =
        let name = consume Identifier "Expect variable name."
        let isMutable = matchAny [ Mutable ]

        let initializer =
            if matchAny [ Equal ] then
                Some(this.Expression())
            elif not isMutable then
                error (peek ()) "Expect '=' after 'var'. Immutable variables must be initialized."
            else
                None

        consume Semicolon "Expect line break or ';' after variable declaration." |> ignore
        VarStmt(name, initializer, isMutable)

    member this.ExpressionStatement() : Stmt option =
        let expr = this.Expression()
        consume Semicolon "Expect line break or ';' after expression." |> ignore
        Some(ExpressionStmt expr)

    member this.Block() : Stmt list =
        let statements = ResizeArray<Stmt>()
        while not (check RightBrace) && not (isAtEnd ()) do
            match this.Declaration() with
            | Some stmt -> statements.Add stmt
            | None -> ()
        consume RightBrace "Expect '}' after block." |> ignore
        List.ofSeq statements

    member this.Assignment() : Expr =
        let expr = this.Pipe()

        if matchAny [ Equal ] then
            let equals = previous ()
            let value = this.Assignment()

            match expr with
            | Variable name -> Assign(name, value)
            | Get(obj, name) -> Set(obj, name, value)
            | Index(obj, index, bracket) -> IndexSet(obj, index, value, bracket)
            | _ -> error equals "Invalid assignment target."
        else
            expr

    member this.Pipe() : Expr =
        // `a |> f` desugars directly to the call `f(a)` at parse time --
        // there's no separate Pipe AST node or interpreter/codegen
        // support needed. Chains left-associatively: `a |> f |> g` is
        // `g(f(a))`.
        let mutable expr = this.Comma()
        while matchAny [ Pipe ] do
            let operator = previous ()
            let right = this.Comma()
            if right.IsVariable then
                expr <- Call(right, [ expr ])
            else
                error operator "Expect a function reference after '|>'." |> ignore
        expr

    member this.Comma() : Expr =
        // While disabled (inside a `[...]` vector literal), a single
        // element is just a ternary -- the comma between elements is the
        // vector loop's own separator, not this operator, and is left
        // untouched for that loop to consume.
        if not commaAsOperator then
            this.Ternary()
        else
            let mutable expr = this.Ternary()
            while matchAny [ Comma ] do
                let operator = previous ()
                let right = this.Ternary()
                expr <- Binary(expr, operator, right)
            expr

    member this.Ternary() : Expr =
        let expr = this.NullCoalescing()

        if matchAny [ QuestionMark ] then
            let leftOperator = previous ()
            let middle = this.Expression()
            let rightOperator = consume Colon "Expect ':' in ternary operator."
            let right = this.Expression()
            Ternary(expr, leftOperator, middle, rightOperator, right)
        elif matchAny [ QuestionMarkColon ] then
            let leftOperator = previous ()
            let rightOperator = previous ()
            let right = this.Expression()
            Ternary(expr, leftOperator, expr, rightOperator, right)
        else
            expr

    member this.NullCoalescing() : Expr =
        let mutable expr = this.LogicOr()
        while matchAny [ DoubleQuestionMark ] do
            let operator = previous ()
            let right = this.LogicOr()
            expr <- Binary(expr, operator, right)
        expr

    member this.LogicOr() : Expr =
        let mutable expr = this.LogicAnd()
        while matchAny [ Or ] do
            let operator = previous ()
            let right = this.LogicAnd()
            expr <- Logical(expr, operator, right)
        expr

    member this.LogicAnd() : Expr =
        let mutable expr = this.Equality()
        while matchAny [ And ] do
            let operator = previous ()
            let right = this.Equality()
            expr <- Logical(expr, operator, right)
        expr

    member this.Equality() : Expr =
        let mutable expr = this.Comparison()
        while matchAny [ BangEqual; EqualEqual ] do
            let operator = previous ()
            let right = this.Comparison()
            expr <- Binary(expr, operator, right)
        expr

    member this.Comparison() : Expr =
        let mutable expr = this.Addition()
        while matchAny [ Greater; GreaterEqual; Less; LessEqual ] do
            let operator = previous ()
            let right = this.Addition()
            expr <- Binary(expr, operator, right)
        expr

    member this.Addition() : Expr =
        let mutable expr = this.Multiplication()
        while matchAny [ Minus; Plus ] do
            let operator = previous ()
            let right = this.Multiplication()
            expr <- Binary(expr, operator, right)
        expr

    member this.Multiplication() : Expr =
        let mutable expr = this.Increment()
        while matchAny [ Slash; Star; Percent; Power ] do
            let operator = previous ()
            let right = this.Increment()
            expr <- Binary(expr, operator, right)
        expr

    member this.Increment() : Expr =
        if matchAny [ PlusPlus; MinusMinus ] then
            let operator = previous ()
            let right = this.Unary()
            if right.IsVariable then
                Unary(operator, right)
            else
                error operator "Invalid increment/decrement target."
        else
            this.Unary()

    member this.Unary() : Expr =
        if matchAny [ Bang; Minus ] then
            let operator = previous ()
            let right = this.Unary()
            Unary(operator, right)
        else
            this.Call()

    member this.Call() : Expr =
        let mutable expr = this.CallHead()
        let mutable more = true
        while more do
            if matchAny [ Dot ] then
                let name = consume Identifier "Expect property name after '.'."
                expr <- this.FinishPropertyAccess(Get(expr, name))
            elif check LeftBracket && isAdjacent (previous ()) (peek ()) then
                let bracket = advance ()
                let index = this.Expression()
                consume RightBracket "Expect ']' after index." |> ignore
                expr <- Index(expr, index, bracket)
            else
                more <- false
        expr

    member this.CallHead() : Expr =
        // No call takes parentheses. `f()` is the explicit zero-arg
        // marker; `f a`, `f a, b` (fixed-arity, comma-separated, no
        // wrapping parens) are calls with arguments; a bare `f` with
        // nothing recognizable as an argument following it is just a
        // value reference, not a call. `f[0]` (no space) is postfix
        // indexing instead, handled by Call()'s own loop once this
        // returns a bare Variable -- see startsArgumentAfter.
        if check Identifier then
            if checkAt 1 LeftParen && checkAt 2 RightParen then
                let name = advance ()
                advance () |> ignore
                advance () |> ignore
                Call(Variable name, [])
            elif startsArgumentAfter (peek ()) (peekAt 1) then
                let name = advance ()
                let arguments = ResizeArray<Expr>()
                arguments.Add(this.Argument())
                while matchAny [ Comma ] do
                    arguments.Add(this.Argument())
                Call(Variable name, List.ofSeq arguments)
            else
                this.Primary()
        else
            let expr = this.Primary()
            // `super.method` is one combined primary (see Primary()) that,
            // like any other property access, may itself be immediately
            // called.
            if expr.IsSuperExpr then this.FinishPropertyAccess(expr) else expr

    member this.FinishPropertyAccess(expr: Expr) : Expr =
        // expr is a Get or a Super -- same zero-arg/argument-start call
        // detection as CallHead(), just anchored on whatever comes right
        // after the property/method name instead of after a bare
        // identifier. `previous()` is that property/method name token
        // (or Super's `method`), used only for the adjacent-`[`-is-
        // indexing carve-out (see startsArgumentAfter).
        let nameToken = previous ()
        if check LeftParen && checkAt 1 RightParen then
            advance () |> ignore
            advance () |> ignore
            Call(expr, [])
        elif startsArgumentAfter nameToken (peek ()) then
            let arguments = ResizeArray<Expr>()
            arguments.Add(this.Argument())
            while matchAny [ Comma ] do
                arguments.Add(this.Argument())
            Call(expr, List.ofSeq arguments)
        else
            expr

    member this.Argument() : Expr =
        // A grouped expression, a nested call, or a bare primary --
        // Call() already covers all three, falling through to Primary(),
        // which handles "(" expression ")" as a Grouping.
        //
        // Bug found while writing docs/PLAN-0.2.md Phase 5's array-stdlib
        // prelude (`map fn, v`, with `fn` a lambda in *non-last* position):
        // a lambda argument's own body is parsed via the *full* Expression()
        // chain (through Comma()), which -- unless suppressed, exactly like
        // `[...]`'s own element-parsing already suppresses it for itself --
        // treats a bare comma as its own operator. Without this, `map (x)
        // -> x * 2, v` had the lambda's unparenthesized body swallow the `,
        // v` that was meant to be this call's *second* argument, silently
        // producing a 1-argument call. `Grouping`'s own explicit `(...)`
        // re-suppresses/re-enables around itself (see Primary()), so a
        // parenthesized comma-tuple passed as one argument (`f (a, b)`)
        // still works exactly as before -- only an unparenthesized nested
        // expression (a lambda body, here) is affected.
        let previousCommaAsOperator = commaAsOperator
        commaAsOperator <- false
        try
            this.Call()
        finally
            commaAsOperator <- previousCommaAsOperator

    member this.Primary() : Expr =
        if matchAny [ False ] then Literal(BoolValue false)
        elif matchAny [ True ] then Literal(BoolValue true)
        elif matchAny [ Nil ] then Literal NilValue
        elif matchAny [ Break ] then BreakExpr(previous ())
        elif matchAny [ Continue ] then ContinueExpr(previous ())
        elif matchAny [ Underscore ] then Ignore
        elif matchAny [ Self ] then SelfExpr(previous ())
        elif matchAny [ Super ] then
            let keyword = previous ()
            consume Dot "Expect '.' after 'super'." |> ignore
            let method = consume Identifier "Expect superclass method name."
            SuperExpr(keyword, method)
        elif matchAny [ Number; String ] then
            Literal(literalValueOf (previous ()))
        elif matchAny [ Identifier ] then
            Variable(previous ())
        elif check LeftParen && isLambdaAhead () then
            advance () |> ignore // '('
            let parameters = ResizeArray<Token>()
            if not (check RightParen) then
                let mutable more = true
                while more do
                    parameters.Add(consume Identifier "Expect parameter name.")
                    more <- matchAny [ Comma ]
            consume RightParen "Expect ')' after lambda parameters." |> ignore
            let arrow = consume Arrow "Expect '->' after lambda parameters."
            let body = this.Expression()
            Lambda(List.ofSeq parameters, arrow, body)
        elif matchAny [ LeftParen ] then
            // A parenthesized sub-expression is always self-delimiting --
            // its own closing ')' is the real terminator, so the comma
            // operator is always available inside, regardless of whatever
            // outer context suppressed it (a vector literal's elements, or
            // -- see Argument()'s doc comment -- a call's own unparenthesized
            // argument list). Restored afterward so the outer context's own
            // suppression (if any) resumes correctly once these parens close.
            let previousCommaAsOperator = commaAsOperator
            commaAsOperator <- true
            let expr =
                try
                    this.Expression()
                finally
                    commaAsOperator <- previousCommaAsOperator
            consume RightParen "Expect ')' after expression." |> ignore
            Grouping expr
        elif matchAny [ LeftBracket ] then
            let bracket = previous ()
            let previousCommaAsOperator = commaAsOperator
            commaAsOperator <- false
            try
                if check RightBracket then
                    advance () |> ignore
                    Vector []
                else
                    // `...expr` (docs/PLAN-0.2.md decision 7) is only ever
                    // a vector-literal element, never a cons item or a
                    // comprehension body -- so a leading '...' on the
                    // first element unambiguously rules out the '|'
                    // lookahead below before it even runs.
                    let parseVectorElement () =
                        if matchAny [ Ellipsis ] then
                            let ellipsis = previous ()
                            Spread(this.Expression(), ellipsis)
                        else
                            this.Expression()
                    let first = parseVectorElement ()
                    let firstIsSpread =
                        match first with
                        | Spread _ -> true
                        | _ -> false
                    if not firstIsSpread && matchAny [ VerticalBar ] then
                        // Cons ([item | list]) and list comprehensions
                        // ([expr | x <- xs]) share this `[expr |` prefix,
                        // told apart by lookahead on the generator marker
                        // `<-` (docs/PLAN-0.2.md decision 2).
                        if check Identifier && checkAt 1 LeftArrow then
                            let variable = advance ()
                            advance () |> ignore // '<-'
                            let source = this.Expression()
                            consume RightBracket "Expect ']' after list comprehension." |> ignore
                            ListComprehension(first, variable, source, bracket)
                        else
                            let list = this.Expression()
                            consume RightBracket "Expect ']' after cons." |> ignore
                            Cons(first, list, bracket)
                    else
                        let values = ResizeArray<Expr>()
                        values.Add(first)
                        while matchAny [ Comma ] do
                            values.Add(parseVectorElement ())
                        consume RightBracket "Expect ']' after vector elements." |> ignore
                        Vector(List.ofSeq values)
            finally
                // Bug fix vs. poc: poc restores comma_as_operator with a
                // plain assignment *after* the elements loop, so a parse
                // error partway through (e.g. `[1, 2, bad+]`) skips that
                // restore entirely, leaving the comma operator disabled
                // for the rest of the file. Guaranteed here regardless of
                // whether parsing the elements succeeded.
                commaAsOperator <- previousCommaAsOperator
        else
            error (peek ()) "Expect expression."

    member this.Synchronize() : unit =
        advance () |> ignore
        let mutable stop = false
        while not stop && not (isAtEnd ()) do
            if (previous ()).Type = Semicolon then
                stop <- true
            elif [ Class; Fun; Var; For; Return ] |> List.contains (peek ()).Type then
                stop <- true
            else
                advance () |> ignore

/// Parses `tokens` into a list of statements plus any parse errors.
/// Parsing never throws out to the caller -- a malformed statement is
/// recorded as a `ParseError` and parsing resumes at the next likely
/// statement boundary, matching `poc`'s "report and keep going" approach.
let parse (tokens: Token list) : Stmt list * ParseError list =
    ParserState(Array.ofList tokens).Parse()

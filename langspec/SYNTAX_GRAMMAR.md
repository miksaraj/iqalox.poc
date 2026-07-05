# Syntax Grammar for Iqalox
*WIP*
* Missing at least array and hash map specific syntax and the new
    standard library statements
* There is no `if`/`while` statement: the chainable ternary operator
    (see `expression`/`ternary` below) replaces `if`/`else` entirely,
    including for statement-like branches such as `break`/`continue`
    (implemented as expressions, not statements — see
    `docs/PLAN-0.1-POC.md` decision 1); `while` was removed from the
    language outright (`for` is the only loop construct).
* No function call takes parentheses (builtin or user-defined) — see the
    `call`/`arguments`/`argument` rules below. Parens still appear for:
    grouping a compound argument (`fact (n - 1)`), the explicit zero-arg
    call marker (`count()`), and function *declarations'* parameter lists
    (`funDecl`/`function`, unchanged). `print`/`concat` are ordinary
    builtin function bindings (not keywords), called through this same
    grammar — there is no `printStmt`/`concatStmt`.
* `call` chains `.` member access/method calls off of a `call_head` (an
    `IDENTIFIER`, optionally with its own `arguments`, or any other
    `primary` — including a `self` or `super.method` reference). Each `.`
    step is itself optionally followed by `arguments`, the same zero-arg
    `()`/comma-separated/grouped-argument rules as everywhere else — e.g.
    `duck.quack()`, `math.square 3`, `self.name = name`. Classes are
    implemented (`classDecl` below); getters/setters and mixins/traits are
    explicitly out of scope for 0.1-poc (0.2, `docs/PLAN-0.1-POC.md`
    decision 5) — plain field access only, no forced accessor methods.
* Chaining `.method()` straight onto a call that itself takes arguments is
    ambiguous in this paren-free grammar and currently binds to the
    argument, not the outer call (`f x.y` parses as `f(x.y)`, and by the
    same rule `B "Bea".greet()` parses as `B(("Bea").greet())`, not
    `(B("Bea")).greet()`) — bind to a variable first, or wrap the call in
    its own parens (`(B "Bea").greet()`), to chain immediately. See
    `docs/PLAN-0.1-POC.md` §1/§2 for the full explanation; not fixed, since
    a real fix needs a grammar decision, not just a parser patch.
* The full expression-precedence chain below (`assignment` down to
    `primary`) is current as of this writing, including `comma`,
    `null_coalescing`, and the elvis `?:` form of `ternary`. See the root
    `README.md`'s precedence table for the same chain presented
    tightest-to-loosest instead of loosest-to-tightest.
* `%` (modulo) and `^` (power) sit at the same precedence level as `/`/`*`
    (both left-associative), inside `multiplication` below.
* No `undef` literal exists in 0.1-poc (an earlier draft of `primary` listed
    one). "Accessing an uninitialized variable is a runtime error (implicit
    `undef`, not implicit `nil`)" is `ROADMAP.md`'s `0.1` scope, one step
    past this PoC — not implemented here, so left out of the grammar until
    it lands.
##
    program         → declaration* EOF ;
    
    declaration     → classDecl
                    | funDecl
                    | varDecl
                    | statement ;
    classDecl       → "class" IDENTIFIER ( "extends" IDENTIFIER )?
                    "{" function* "}" ;
    funDecl         → "fun" function ;
    function        → IDENTIFIER "(" parameters? ")" block ;
    parameters      → IDENTIFIER ( "," IDENTIFIER )* ;
    varDecl         → "var" IDENTIFIER "mut"? ( "=" expression )? ";?" ;
    
    statement       → exprStmt
                    | forStmt
                    | returnStmt
                    | block ;
                    
    exprStmt        → expression ";?" ;                    
    forStmt         → "for" "(" ( varDecl | exprStmt | ";" )
                                expression? ";?"
                                expression? ")" statement ;
    returnStmt      → "return" expression? ";?" ;
    block           → "{" declaration* "}" ;
    
    expression      → assignment ;

    assignment      → ( call "." )? IDENTIFIER "=" assignment
                    | pipe ;
    pipe            → comma ( "|>" IDENTIFIER )* ;
    comma           → ternary ( "," ternary )* ;

    ternary         → null_coalescing ( "?" expression ":" expression
                                       | "?:" expression )? ;
    null_coalescing → logic_or ( "??" logic_or )* ;
    logic_or        → logic_and ( "or" logic_and )* ;
    logic_and       → equality ( "and" equality )* ;
    equality        → comparison ( ( "!=" | "==" ) comparison )* ;
    comparison      → addition ( ( ">" | ">=" | "<" | "<=" ) addition )* ;
    addition        → multiplication ( ( "-" | "+" ) multiplication )* ;
    multiplication  → increment ( ( "/" | "*" | "%" | "^" ) increment )* ;
    increment       → ( "++" | "--" ) unary | unary ;

    unary           → ( "!" | "-" ) unary | call ;
    call            → call_head ( "." IDENTIFIER arguments? )* ;
    call_head       → IDENTIFIER arguments?
                    | primary ;
    arguments       → "(" ")"
                    | argument ( "," argument )* ;
    argument        → "(" expression ")"
                    | primary
                    | call ;
    primary         → "true" | "false" | "nil" | "self"
                    | "break" | "continue" | "_"
                    | NUMBER | STRING | IDENTIFIER | "(" expression ")"
                    | "[" ( expression ( "," expression )* )? "]"
                    | "super" "." IDENTIFIER ;

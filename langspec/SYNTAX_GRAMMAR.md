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
* The `assignment`/`ternary`/logical/`null_coalescing` chain below is still
    stale relative to `src/parser.py` in its details (predates most of the
    features documented in `docs/PLAN-0.1-POC.md`) — the shape of `call`,
    `pipe`, and the declaration/statement productions are current as of
    this writing; the rest of the expression-precedence chain still needs
    a real pass (in particular: `null_coalescing` isn't shown at all yet,
    and `ternary`'s own definition doesn't reflect the elvis `?:` form).
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
    
    assignment      → ( call "." )? IDENTIFIER "=" assignment ( ternary )?
                    | pipe ;
    pipe            → comma ( "|>" IDENTIFIER )* ;
                    
    ternary         → expression "?" expression? ":" expression ;
    logic_or        → logic_and ( "or" logic_and )* ;
    logic_and       → equality ( "and" equality )* ;
    equality        → comparison ( ( "!=" | "==" ) comparison )* ;
    comparison      → addition ( ( ">" | ">=" | "<" | "<=" ) addition )* ;
    addition        → multiplication ( ( "-" | "+" ) multiplication )* ;
    multiplication  → unary ( ( "/" | "*" ) unary )* ;
    
    unary           → ( "!" | "-" | "++" | "--" ) unary | call ;
    call            → call_head ( "." IDENTIFIER arguments? )* ;
    call_head       → IDENTIFIER arguments?
                    | primary ;
    arguments       → "(" ")"
                    | argument ( "," argument )* ;
    argument        → "(" expression ")"
                    | primary
                    | call ;
    primary         → "true" | "false" | "nil" | "self" | "undef"
                    | "break" | "continue" | "_"
                    | NUMBER | STRING | IDENTIFIER | "(" expression ")"
                    | "[" ( expression ( "," expression )* )? "]"
                    | "super" "." IDENTIFIER ;

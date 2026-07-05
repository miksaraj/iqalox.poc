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
* `call` only chains off a bare `IDENTIFIER` for now (`call → IDENTIFIER
    arguments?`) — no `.` member-access chaining yet (`instance.method
    args`); that needs classes (not yet implemented) to have a receiver
    to chain off of.
* The `assignment`/`comma`/`ternary`/logical/`null_coalescing` chain below
    is still stale relative to `src/parser.py` (predates most of the
    features documented in `docs/PLAN-0.1-POC.md`) — the shape of `call`
    and the declaration/statement productions are current as of this
    writing; the expression-precedence chain still needs a real pass.
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
                    | logic_or ;
                    
    ternary         → expression "?" expression? ":" expression ;
    logic_or        → logic_and ( "or" logic_and )* ;
    logic_and       → equality ( "and" equality )* ;
    equality        → comparison ( ( "!=" | "==" ) comparison )* ;
    comparison      → addition ( ( ">" | ">=" | "<" | "<=" ) addition )* ;
    addition        → multiplication ( ( "-" | "+" ) multiplication )* ;
    multiplication  → unary ( ( "/" | "*" ) unary )* ;
    
    unary           → ( "!" | "-" | "++" | "--" ) unary | call ;
    call            → IDENTIFIER arguments? ;
    arguments       → "(" ")"
                    | argument ( "," argument )* ;
    argument        → "(" expression ")"
                    | primary
                    | call ;
    primary         → "true" | "false" | "nil" | "self" | "undef"
                    | "break" | "continue"
                    | NUMBER | STRING | IDENTIFIER | "(" expression ")"
                    | "[" ( expression ( "," expression )* )? "]"
                    | "super" "." IDENTIFIER ;

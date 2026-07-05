# Syntax Grammar for Iqalox
*WIP*
* Missing at least array and hash map specific syntax and the new
    standard library statements
* There is no `if`/`while` statement: the chainable ternary operator
    (see `expression`/`ternary` below) replaces `if`/`else` entirely,
    including for statement-like branches such as `break`/`continue`;
    `while` was removed from the language outright (`for` is the only
    loop construct). `printStmt` below is stale — `print`/`concat` are
    being promoted to ordinary builtin functions rather than statement
    keywords; this production still needs a rewrite once that lands
    (see `docs/PLAN-0.1-POC.md`).
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
    varDecl         → "var" IDENTIFIER ( "=" expression )? ";?" ;
    
    statement       → exprStmt
                    | forStmt
                    | printStmt
                    | returnStmt
                    | block ;
                    
    exprStmt        → expression ";?" ;                    
    forStmt         → "for" "(" ( varDecl | exprStmt | ";" )
                                expression? ";?"
                                expression? ")" statement ;
    printStmt       → "print" expression ";?" ;
    returnStmt      → "return" expression? ";?" ;
    continueStmt    → "continue" ";?" ;
    breakStmt       → "break" ";?" ;
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
    call            → primary ( "(" arguments? ")" | "." IDENTIFIER )* ;
    arguments       → expression ( "," expression )* ;
    primary         → "true" | "false" | "nil" | "self" | "undef"
                    | NUMBER | STRING | IDENTIFIER | "(" expression ")"
                    | "super" "." IDENTIFIER ;

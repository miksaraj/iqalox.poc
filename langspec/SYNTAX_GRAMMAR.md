# Syntax Grammar for Iqalox
*WIP*
* Missing at least array and hash map specific syntax and the new
    standard library statements
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
                    | ifStmt
                    | printStmt
                    | returnStmt
                    | whileStmt
                    | block ;
                    
    exprStmt        → expression ";?" ;                    
    forStmt         → "for" "(" ( varDecl | exprStmt | ";" )
                                expression? ";?"
                                expression? ")" statement ;
    ifStmt          → "if" "(" expression ")" statement ( "else" statement )? ;
    printStmt       → "print" expression ";?" ;
    returnStmt      → "return" expression? ";?" ;
    continueStmt    → "continue" ";?" ;
    breakStmt       → "break" ";?" ;
    whileStmt       → "while" "(" expression ")" statement ; 
    block           → "{" declaration* "}" ;
    
    expression      → assignment ;
    
    assignment      → ( call "." )? IDENTIFIER "=" assignment ( ternary )?
                    | logic_or ;
                    
    ternary         → expression "?" expression ":" expression ;
    logic_or        → logic_and ( "or" logic_and )* ;
    logic_and       → equality ( "and" equality )* ;
    equality        → comparison ( ( "!=" | "==" ) comparison )* ;
    comparison      → addition ( ( ">" | ">=" | "<" | "<=" ) addition )* ;
    addition        → multiplication ( ( "-" | "+" ) multiplication )* ;
    multiplication  → unary ( ( "/" | "*" ) unary )* ;
    
    unary           → ( "!" | "-" | "++" | "--" ) unary | call ;
    call            → primary ( "(" arguments? ")" | "." IDENTIFIER )* ;
    arguments       → expression ( "," expression )* ;
    primary         → "true" | "false" | "nil" | "this" | "undef"
                    | NUMBER | STRING | IDENTIFIER | "(" expression ")"
                    | "super" "." IDENTIFIER ;

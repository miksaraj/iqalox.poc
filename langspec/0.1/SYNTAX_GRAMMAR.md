# Syntax Grammar for Iqalox v0.1 #
*WIP*
* Missing at least array and hash map specific syntax
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
    
    assignment      → ( call "." )? IDENTIFIER "=" assignment
                    | logic_or ;
                    
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

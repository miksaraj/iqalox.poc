# Iqalox (Proof of Concept) #

This is a tree-walk interpretation implementation of Iqalox language in Python. It is a proof of concept preceding the
proper bytecode compiler implementation.

"*Iqalox*" is a play on the Kalaallisut word for Arctic char, *Eqaluk*, and *Lox* from Bob Nystrom's
[Crafting Interpreters](http://craftinginterpreters.com/). The language presented here is based on Nystrom's Lox
but mutated and extended to suit the author's preferences and interests.

You will find the **syntax grammar** in `langspec`. The **lexical grammar** and **precedence rules** are presented below.

_Please note: Iqalox is in planning phase, so no implementation has been written as of yet._

## Lexical Grammar ##

    NUMBER          → DIGIT+ ( "." DIGIT+ )? ;
    STRING          → '"' <any char except '"'>* '"' ;
    IDENTIFIER      → ALPHA ( ALPHA | DIGIT )* ;
    ALPHA           → 'a' ... 'z' | 'A' ... 'Z' | '_' ;
    DIGIT           → '0' ... '9' ;
    

## Precedence Rules ##

|      Name      |       Operators       | Associates |
|:--------------:|:---------------------:|:----------:|
|     Unary      |        `!` `-`        |   Right    |
|   Increment    |       `++` `--`       |   Right    |
| Multiplication |        `/` `*`        |    Left    |
|    Addition    |        `-` `+`        |    Left    |
|   Comparison   |   `>` `>=` `<` `<=`   |    Left    |
|    Equality    |       `==` `!=`       |    Left    |

# Iqalox (Proof of Concept) #

This is a tree-walk interpretation implementation of Iqalox language in Python. It is a proof of concept preceding the
proper bytecode compiler implementation.

"*Iqalox*" is a play on the Kalaallisut word for Arctic char, *Eqaluk*, and *Lox* from Bob Nystrom's
[Crafting Interpreters](http://craftinginterpreters.com/). The language presented here is based on Nystrom's Lox
but mutated and extended to suit the author's preferences and interests.

You will find the **syntax grammar** in `langspec`. The **lexical grammar** and **precedence rules** are presented below.

This repository holds a working tree-walk interpreter (in Python) implementing the
current `0.1-poc` milestone — see `ROADMAP.md` for the version plan and
`docs/LANGUAGE.md` for the full language documentation (with examples and known
limitations).

## Lexical Grammar ##

    NUMBER          → DIGIT+ ( "." DIGIT+ )? ;
    STRING          → '"' <any char except '"'>* '"' | "'" <any char except "'">* "'" ;
    IDENTIFIER      → ALPHA ( ALPHA | DIGIT )* ;
    ALPHA           → 'a' ... 'z' | 'A' ... 'Z' | '_' ;
    DIGIT           → '0' ... '9' ;
    

## Precedence Rules ##

|       Name       |       Operators       | Associates |
|:----------------:|:---------------------:|:----------:|
|      Unary       |        `!` `-`        |   Right    |
|    Increment     |       `++` `--`       |   Right    |
|  Multiplication  |    `/` `*` `%` `^`    |    Left    |
|     Addition     |        `-` `+`        |    Left    |
|    Comparison    |  `>` `>=` `<` `<=`    |    Left    |
|     Equality     |       `==` `!=`       |    Left    |
|   Logical AND    |         `and`         |    Left    |
|    Logical OR    |         `or`          |    Left    |
| Null-coalescing  |          `??`          |    Left    |
|     Ternary      |    `? :` / `?:`       |   Right    |
|      Comma       |          `,`          |    Left    |
|       Pipe       |         `\|>`         |    Left    |
|    Assignment    |          `=`          |   Right    |

Listed tightest-binding (evaluated first) to loosest. `%`/`^` share
`*`/`/`'s precedence level rather than binding tighter, unlike some other
languages. `?:` is the elvis short form of the ternary (`a ?: b` ≡
`a ? a : b`) at the same precedence as the full `? :` form.

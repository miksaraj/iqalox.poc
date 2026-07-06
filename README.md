# Iqalox #

Iqalox is a programming language based on Lox, from Bob Nystrom's
[Crafting Interpreters](http://craftinginterpreters.com/), mutated and extended to suit the author's
preferences and interests. "*Iqalox*" is a play on the Kalaallisut word for Arctic char, *Eqaluk*, and *Lox*.

This repository holds multiple implementations across Iqalox's versions:

- `poc/` — the original `0.1-poc` proof of concept: a tree-walk interpreter in Python. Now frozen/reference,
  since `0.1` below has reached feature parity with it.
- `compiler/` + `vm/` — `0.1`, the real implementation: a compiler frontend (F#) that compiles to a bytecode
  format executed by a stack-based virtual machine (modern C++). The current, primary implementation.

See `ROADMAP.md` for the version plan, `docs/LANGUAGE.md` for the current (`0.1`) language reference, and
`docs/LANGUAGE-POC.md` for the frozen `0.1-poc`-era one — both with examples and known limitations.

You will find the **syntax grammar** in `langspec`. The **lexical grammar** and **precedence rules** are presented below.

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

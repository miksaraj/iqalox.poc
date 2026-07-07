# Iqalox #

Iqalox is a programming language based on Lox, from Bob Nystrom's
[Crafting Interpreters](http://craftinginterpreters.com/), mutated and extended to suit the author's
preferences and interests. "*Iqalox*" is a play on the Kalaallisut word for Arctic char, *Eqaluk*, and *Lox*.

`0.1` is the current, primary implementation: a compiler frontend (F#, `compiler/`) that compiles `.iqx`
source to a bytecode file, executed by a stack-based virtual machine (modern C++23, `vm/`). This repository
also keeps `poc/` around — the original `0.1-poc` proof of concept, a Python tree-walk interpreter — as a
frozen, working reference implementation, now that `0.1` has reached feature parity with it.

## Getting started

**Option 1: download a release.** Grab the archive for your platform from the
[Releases page](https://github.com/miksaraj/iqalox.poc/releases) — each one bundles both binaries
(`iqaloxc`, `iqaloxvm`) plus a handful of example `.iqx` scripts, and needs nothing else installed.

**Option 2: build from source.** You'll need the [.NET 10 SDK](https://dotnet.microsoft.com/download)
and a C++23 compiler (GCC 13+ or Clang 18+) plus [CMake](https://cmake.org/) 3.20+:

```sh
# Build the compiler
dotnet build compiler/src/Iqaloxc.fsproj

# Build the VM
cmake -S vm -B vm/build -DCMAKE_BUILD_TYPE=Release
cmake --build vm/build --target iqaloxvm -j
```

Either way, running a program is a two-step pipeline — compile to bytecode, then run it:

```sh
iqaloxc langspec/versions/0.1/examples/classes.iqx classes.iqbc
iqaloxvm classes.iqbc
```

(If you built from source instead of downloading a release, replace `iqaloxc` with
`dotnet run --project compiler/src/Iqaloxc.fsproj --` and `iqaloxvm` with `vm/build/iqaloxvm`.)

See `docs/LANGUAGE.md` for the full `0.1` language reference (with plenty of runnable examples and a list
of known limitations), `ROADMAP.md` for the version plan, and `docs/LANGUAGE-POC.md` for the frozen
`0.1-poc`-era reference. You'll find the **syntax grammar** in `langspec/`; the **lexical grammar** and
**precedence rules** are presented below.

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

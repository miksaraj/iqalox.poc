# The Iqalox Language (0.1)

> **For older versions, see:** [`docs/LANGUAGE-POC.md`](LANGUAGE-POC.md) for
> `0.1-poc` (the Python tree-walk interpreter this version supersedes as
> the primary reference — `poc/` stays in the repo as a working reference
> implementation, per `CLAUDE.md`). Each version gets its own
> `LANGUAGE-<version>.md` this way as Iqalox evolves; this file always
> describes the current, actively-maintained version.

This is the complete reference for Iqalox as implemented by `0.1` — a
compiler frontend (`compiler/`, F#) that compiles `.iqx` source to a
bytecode file, executed by a stack-based virtual machine (`vm/`, C++23).
It documents what the language **actually does today**, with runnable
examples and an explicit list of known limitations. `0.1` is, by design,
everything `0.1-poc` had — same syntax, same semantics, same example
programs — plus four additions (§4, §10, and noted throughout): `undef`/
must-assign-before-read, string escape sequences, compile-time
immutability enforcement, and self-referencing classes. See `ROADMAP.md`
for what's planned beyond this milestone, `docs/PLAN-0.1.md` for the
day-to-day `0.1` implementation log, and `docs/PLAN-0.1-POC.md` for the
original `0.1-poc` design decisions this version inherits.

Every code sample below is valid Iqalox and, unless marked otherwise,
compiles and runs successfully via `compiler/src/Iqaloxc.fsproj` (or
`iqaloxc`, once built) piped into `vm/build/iqaloxvm`. Longer, complete
programs live under `langspec/examples/*.iqx` — the same fixtures run
against `poc/` and checked byte-for-byte identical via
`scripts/conformance-test.sh` (`docs/PLAN-0.1.md` Phase 9).

## Table of contents

1. [Introduction and classification](#1-introduction-and-classification)
2. [Lexical structure](#2-lexical-structure)
3. [Values and types](#3-values-and-types)
4. [Variables and mutability](#4-variables-and-mutability)
5. [Operators and precedence](#5-operators-and-precedence)
6. [Control flow](#6-control-flow)
7. [Functions and closures](#7-functions-and-closures)
8. [The call syntax](#8-the-call-syntax-no-parentheses)
9. [The pipe operator](#9-the-pipe-operator)
10. [Classes and objects](#10-classes-and-objects)
11. [The standard library (so far)](#11-the-standard-library-so-far)
12. [Errors](#12-errors)
13. [Known limitations](#13-known-limitations)
14. [Grammar and precedence reference](#14-grammar-and-precedence-reference)

## 1. Introduction and classification

Iqalox is Lox — the small teaching language from Bob Nystrom's *Crafting
Interpreters* — mutated and extended by the repository owner into a
personal language project. `0.1` is the first real, non-proof-of-concept
implementation: a **compiler frontend plus a bytecode virtual machine**,
superseding `0.1-poc`'s tree-walk interpreter (see `ROADMAP.md`'s
architecture note).

Classifying `0.1` along the axes commonly used to describe programming
languages — all statements below describe this implementation as it
exists today, not a permanent commitment for later Iqalox versions:

- **Paradigm**: primarily **imperative/procedural**, with **class-based
  object orientation** (`class`, `extends`, `self`, `super`) and a
  meaningful vein of **functional-language influence** — functions are
  first-class values, closures capture their defining environment, and the
  pipe operator (`|>`) encourages composing calls left-to-right the way
  functional languages do. There is no logic- or constraint-programming
  aspect.
- **Typing discipline**: **dynamically typed**. There is no type
  declaration syntax anywhere in the grammar — variables, parameters, and
  fields hold whatever value is assigned, and type errors (e.g. `"a" + 1`)
  surface as runtime errors, not at compile time. Within that, Iqalox
  leans **strong**: operators check operand types and raise a runtime
  error rather than silently coercing between them (no string-to-number or
  number-to-string coercion happens for `+`, `-`, `*`, `/`, `%`, `^`, or the
  comparison operators).
- **Memory management**: fully **automatic**, via a real **mark-sweep
  tracing garbage collector** built into the VM (`vm/src/vm.cpp`) — unlike
  `0.1-poc`, which simply delegated to Python's own GC, `0.1`'s VM
  allocates and traces every heap object (strings, vectors, functions,
  closures, classes, instances) itself. There is no manual memory
  management, no pointers, and no language-level concept of ownership or
  lifetimes exposed to Iqalox source code.
- **Execution model**: **ahead-of-time compiled to bytecode, then
  interpreted by a stack-based virtual machine** — source is scanned,
  parsed into an AST (`compiler/src/Ast.fs`), resolved into a bound tree
  with every binding/scope/slot already determined
  (`compiler/src/Bound.fs`), and lowered to a structured bytecode format
  (`compiler/src/Bytecode.fs`, `compiler/src/Codegen.fs`) that the VM
  (`vm/src/vm.cpp`) loads and executes via a fetch-decode-execute loop over
  an explicit value stack and call-frame stack. There is no tree-walking,
  no JIT, and no further optimization pass on the bytecode itself.
- **Evaluation strategy**: **eager (strict)** evaluation everywhere except
  the short-circuiting operators — `and`/`or`, `??`, and the ternary/elvis
  forms — which compile to conditional jumps rather than always evaluating
  every operand (see [§5](#5-operators-and-precedence),
  [§6](#6-control-flow)). Function arguments are fully evaluated before a
  call executes (**call-by-value** for the argument slots themselves;
  since the values passed may be references to heap objects, mutable
  values like vectors are shared by reference).
- **Scoping**: **lexical (static) scoping**, resolved entirely at compile
  time. `compiler/src/Resolver.fs` assigns every local variable a fixed
  stack slot and every non-local reference an upvalue, following the same
  scope/slot/upvalue algorithm as `clox` (*Crafting Interpreters*); the VM
  never does a name-based environment lookup at runtime for locals.
  Closures capture the variables active at the point a function is
  *declared*, not where it's *called* — implemented as `ObjUpvalue`s that
  point directly at a stack slot until that slot goes out of scope, then
  hold a closed-over copy. `super` resolution is **lexical**, the same as
  `0.1-poc` — it resolves to the class the calling method is *written in*,
  never to the calling instance's actual runtime class (see
  [§10](#10-classes-and-objects)).
- **Name-binding / mutability default**: **immutable by default**, now
  **enforced at compile time** rather than only at runtime — `var x = 1`
  followed by `x = 2` is a compile-time error (`docs/PLAN-0.1-POC.md`
  decision 2's runtime-only interim tradeoff is resolved for `0.1`). `var x
  mut = 1` opts in to reassignment. This extends to function parameters
  (also immutable inside the function body) but *not* to object fields,
  which are always freely reassignable — the same deliberately asymmetric
  design point as `0.1-poc` (see [§13](#13-known-limitations)). **New for
  `0.1`**: a mutable variable declared without an initializer (`var x
  mut`) starts as `undef`, not `nil` — reading it before its first
  assignment is a runtime error, not a silent `nil` (see
  [§4](#4-variables-and-mutability)).
- **Object model**: **class-based** (not prototype-based), with **single
  inheritance** via `extends` and **nominal** dispatch — a method call
  resolves against the instance's actual runtime class (`ObjClass`'s
  `methods` map, populated by copying the superclass's methods at
  `extends`-time), with no structural/duck-typed interface concept. The
  object model is currently **partial**: user-defined classes produce real
  objects with method dispatch, but the built-in value types (numbers,
  strings, booleans, vectors) are plain values with **no methods of their
  own** — there's no `"hello".length()`-style access on primitives.
  **New for `0.1`**: a class can now reference itself by name from inside
  its own methods (see [§10](#10-classes-and-objects)).
- **Concurrency model**: **none**. Iqalox is single-threaded with no
  concurrency primitives, no `async`/`await`, and no scheduling model of
  any kind at this stage.
- **Error-handling model**: internally, the compiler front-end accumulates
  scan/parse/resolve/codegen errors as data (not exceptions), and the VM
  signals runtime faults via a C++ exception (`RuntimeError`) caught once
  at the top of `main`, but **none of this is exposed to Iqalox source
  code** — there is no `try`/`catch`/`throw` construct. A runtime error
  anywhere aborts the whole program (see [§12](#12-errors)).
- **Metaprogramming/reflection**: **none**. No `eval`, no reflection API,
  no macros.
- **Call syntax**: the standout, deliberately unusual axis for this
  language — see [§8](#8-the-call-syntax-no-parentheses). No function
  call, builtin or user-defined, takes parentheses; calls are formed by
  **fixed-arity juxtaposition** (`f x`, `f x, y`) reminiscent of ML-family
  languages, but *without* currying or partial application — a call's
  arity is fixed by the callee's declaration, not inferred from how many
  arguments happen to be written.
- **Syntax style**: brace-delimited blocks (`{ ... }`, not
  indentation-sensitive) combined with **implicit statement termination**
  on a newline (à la Python/JavaScript ASI) — either a real `;` or a
  newline ends a statement. Comments come in a shell-style line form
  (`# ...`) and a Pascal/ML-style block form (`<# ... #>`), not C-style
  (`//`, `/* */`).

## 2. Lexical structure

```
NUMBER      → DIGIT+ ( "." DIGIT+ )?
STRING      → '"' <any char except '"'>* '"' | "'" <any char except "'">* "'"
IDENTIFIER  → ALPHA ( ALPHA | DIGIT )*
ALPHA       → 'a'...'z' | 'A'...'Z' | '_'
DIGIT       → '0'...'9'
```

- **Numbers** are always floating-point — there is no separate integer
  type (`5` and `5.0` are the same value). Printing drops a trailing `.0`,
  so `print 5` prints `5`, not `5.0` (see [§11](#11-the-standard-library-so-far)).
- **Strings** may use single or double quotes interchangeably. **New for
  `0.1`**: escape sequences are now processed inside string literals —
  `\n` `\t` `\r` `\\` `\'` `\"` `\0` are recognized; any other character
  after a backslash is a compile-time scan error. `0.1-poc` had no escape
  processing at all (see [§13](#13-known-limitations)).
- **Comments**: `# ...` runs to end of line; `<# ... #>` is a block comment
  and may span multiple lines. Neither nests.
- **Implicit semicolons**: a newline is scanned as a statement terminator,
  exactly like a literal `;`. A blank line (or a comment-only line)
  becomes a harmless empty statement, not an error.
- **Keywords** (cannot be used as identifiers): `and`, `class`, `false`,
  `fun`, `for`, `nil`, `or`, `return`, `super`, `self`, `true`, `var`,
  `extends`, `break`, `continue`, `mut`. (`with`, `module`, `trait`, `use`
  are reserved for future mixin/trait/module support but have no grammar
  or meaning yet.) `print` and `concat` are **not** keywords — see
  [§11](#11-the-standard-library-so-far).

## 3. Values and types

Iqalox has five kinds of user-visible runtime value in `0.1`:

| Type | Literal syntax | Example |
|---|---|---|
| Number (always float) | digits, optional decimal point | `42`, `3.14` |
| String | single- or double-quoted | `"hi"`, `'hi'` |
| Boolean | `true` / `false` | `true` |
| `nil` | `nil` | `nil` |
| Vector | `[ ... ]`, comma-separated | `[1, 2, "three"]` |

Plus two callable/object kinds introduced by functions and classes:
function values (user-defined or native builtins) and class
instances — see [§7](#7-functions-and-closures) and
[§10](#10-classes-and-objects).

**New for `0.1`**: there is also an internal `undef` value (distinct from
`nil`), used to mark a mutable variable that hasn't been assigned yet.
It is never actually observable in a running program — every read of a
variable checks for `undef` first and raises a runtime error immediately,
so it can't be printed, compared, passed to a function, or stored
anywhere (see [§4](#4-variables-and-mutability)).

**Truthiness**: only `nil` and `false` are falsy. Every other value — `0`,
`""` (empty string), and `[]` (empty vector) included — is truthy:

```
print (0 ? "truthy" : "falsy")     # truthy
print ("" ? "truthy" : "falsy")    # truthy
print (nil ? "truthy" : "falsy")   # falsy
```

**Equality** (`==`/`!=`) compares values directly and never raises —
comparing a number to a string, for instance, is a well-defined `false`,
not an error. **Ordering** comparisons (`>`, `>=`, `<`, `<=`) and
**arithmetic** (`+`, `-`, `*`, `/`, `%`, `^`) require both operands to be
numbers and raise a runtime error otherwise — there is no string
comparison or string `+` concatenation (use `concat`,
[§11](#11-the-standard-library-so-far)).

Vectors are **literals only** in `0.1`, same as `0.1-poc`: `[1, 2, 3]`
produces a value, but there is no indexing (`v[0]`), no mutation, no
`length`, and no other vector-manipulation stdlib yet — that's `0.2`
scope (`ROADMAP.md`).

## 4. Variables and mutability

```
var x = 1          # immutable -- must be initialized
var y mut = 1      # mutable -- may be reassigned
y = 2              # OK
x = 2              # compile-time error: 'x' is immutable
var z mut          # OK -- mutable declarations may omit the initializer,
                    # but z starts as undef, not nil (see below)
```

An immutable (`var x`, no `mut`) declaration **must** have an
initializer — `var x` with no `mut` and no `= ...` is a compile-time
error, since there would be no way to ever give it a value. A mutable
declaration may omit the initializer, in which case the variable starts
as `undef`.

**New for `0.1`**: reassigning a variable that wasn't declared `mut` is
now caught at **compile time** — `compiler/src/Resolver.fs` tracks each
binding's mutability and reports an error before any bytecode is even
generated, resolving `0.1-poc`'s documented interim tradeoff
(`docs/PLAN-0.1-POC.md` decision 2) of only catching this at runtime.
Function parameters are immutable inside the function body by the same
rule, with no per-parameter `mut` syntax to opt out.

**New for `0.1`**: reading a mutable variable before it's ever been
assigned (`var z mut` then using `z` with no assignment in between) is a
**runtime** error — `undef` is a distinct value from `nil`, and every
variable read checks for it first. This is deliberately a runtime check,
not a compile-time one: unlike immutability (which depends only on static
structure — was there an initializer, is there a later assignment), a
`var mut` binding's assignedness can depend on control flow the resolver
doesn't model (e.g. a variable assigned inside one branch of a ternary but
not another).

Object fields (`self.x = ...`) are the one exception to all of the above:
they are **always** freely reassignable, with no `mut` concept and no
`undef` state at all (fields simply don't exist until first assigned —
see [§13](#13-known-limitations) for why).

## 5. Operators and precedence

From loosest-binding to tightest (see [§14](#14-grammar-and-precedence-reference)
for the full grammar, and the root `README.md` for the same chain
presented tightest-first):

| Operator(s) | Meaning | Notes |
|---|---|---|
| `=` | assignment | right-associative |
| `\|>` | pipe | desugars to a call, see [§9](#9-the-pipe-operator) |
| `,` | comma | left-associative, sequences expressions |
| `? :` / `?:` | ternary / elvis | see [§6](#6-control-flow) |
| `??` | null-coalescing | `a ?? b` is `a` unless `a` is `nil`, else `b` |
| `or` | logical or | short-circuits |
| `and` | logical and | short-circuits |
| `==` `!=` | equality | never raises |
| `>` `>=` `<` `<=` | comparison | numbers only |
| `-` `+` | additive | numbers only, no string concat |
| `/` `*` `%` `^` | multiplicative | includes modulo and power, numbers only |
| `++` `--` | prefix increment/decrement | mutating, target must be a variable |
| `!` `-` | unary not / negate | |

```
print (1 + 2 * 3)      # 7
print (2 ^ 10)         # 1024
print (7 % 3)          # 1
print (nil ?? "fallback")   # fallback
print (!false)         # true

var x mut = 1
print (++x)            # 2 -- mutates x and evaluates to the new value
print (--x)            # 1
```

Every short-circuiting operator here (`??`, `and`, `or`, ternary/elvis)
compiles to a conditional jump in the emitted bytecode — the right-hand
side's bytecode is only reached, and thus only ever executed, when it's
actually needed. Elvis (`a ?: b`) in particular evaluates its condition
exactly once even though it's also the "then" value, via a jump that
reuses the already-computed value rather than emitting the condition's
bytecode twice.

## 6. Control flow

**There is no `if`/`else` statement and no `while` loop.** Both are
deliberately absent from the language (`docs/PLAN-0.1-POC.md` decision 1;
`while` was removed outright).

The chainable ternary operator replaces `if`/`else` entirely, including
for statement-like branches (side-effecting calls, `break`, `continue`):

```
var category = (n < 0) ? "negative" : (n == 0) ? "zero" : "positive"

for (var i mut = 0; i < 10; ++i) {
    (i == 3) ? continue : (i == 7) ? break : print i
}
```

`?:` is the **elvis** short form: `a ?: b` means "`a` if `a` is truthy,
else `b`" — it reuses the condition as the "then" value instead of writing
it twice:

```
var name mut = false
print (name ?: "anonymous")   # anonymous
name = "Waddles"
print (name ?: "anonymous")   # Waddles
```

`break` and `continue` are **expressions**, not statements — they're
parsed the same way `_` (see below) is, which is exactly what makes them
legal ternary branches. Each compiles to a jump out of (`break`) or back
to the top of (`continue`) the nearest enclosing `for` loop's bytecode;
using either outside of a loop is reported as a compile-time codegen
error rather than crashing anything.

`for` is the only loop construct, with the full three-clause C-style form,
every clause optional:

```
for (var i mut = 0; i < 5; ++i) { print i; }
for (;;) { break; }   # infinite loop, only escape is a break somewhere inside
```

The **ignore operator** `_` is an expression that evaluates to `nil` with
no side effect — useful as an explicit "do nothing" ternary branch:

```
var flag = true
flag ? _ : print "only prints when flag is false"
```

## 7. Functions and closures

```
fun add(a, b) { return a + b; }
var result = add 1, 2   # 3
```

Functions are first-class values. A bare function name with nothing
recognizable as an argument after it is a *reference* to the function, not
a call — this is what lets functions be passed around:

```
fun makeFive() { return 5; }
fun apply(func) { return func(); }   # explicit () -- see §8
print (apply makeFive)               # 5
```

Closures capture their **defining** environment, and that capture is live
(mutations to captured `mut` variables are visible across calls) — the VM
implements this with `ObjUpvalue`, which points directly at the
variable's stack slot for as long as that slot is live, then transitions
to holding a closed-over copy once the enclosing call returns:

```
fun createCounter() {
    var c mut = 0
    fun counter() { c = c + 1; return c; }
    return counter
}
var count = createCounter()
print count()   # 1
print count()   # 2
```

Recursion works via the normal name-binding mechanism (a function's own
name is bound in its enclosing scope before its body runs):

```
fun fact(n) { return (n == 1) ? n : (n * fact (n - 1)); }
print (fact 5)   # 120
```

A function called with the wrong number of arguments, or a non-callable
value called at all, is a runtime error.

## 8. The call syntax (no parentheses)

This is Iqalox's most distinctive departure from Lox (and from most
C-family languages): **no function call — builtin or user-defined — ever
takes parentheses to mark its argument list.** This was a deliberate,
non-negotiable design decision (`docs/PLAN-0.1-POC.md` decision 4), not an
oversight.

- **A single simple argument needs nothing**: `add5 1`, `Duck "Waddles"`,
  `math.square 3`.
- **Multiple arguments are comma-separated, with no wrapping parens**:
  `ifEqualOr 2, 5`. That comma is an argument separator, not the general
  comma operator (the same way the comma operator is suppressed inside a
  `[...]` vector literal).
- **A compound (non-primary) argument needs grouping parens** to mark
  where it ends: `fact (n - 1)`. Without the parens, `fact n - 1` would
  parse as `fact n` (a call) minus `1` — a binary subtraction, not one
  argument.
- **Zero-argument calls keep explicit `()`**: `count()`, `duck.quack()`.
  This is what lets a bare name mean "the function value itself" —
  without it, there'd be no way to distinguish "call `count` with no
  arguments" from "refer to `count` without calling it."
- **Nested calls need no extra parens**, because a fully-parsed call is
  itself just another primary value: `print concat [1, 2]` parses as
  `print(concat([1, 2]))`, with `concat [1, 2]` recognized as one complete
  call that becomes `print`'s single argument.
- Parentheses are **still used** for: grouping a compound argument (above),
  the explicit zero-arg marker (above), and function *declarations'*
  parameter lists (`fun f(a, b) { ... }` is unchanged) — only *call sites*
  lost their parentheses.

`print` and `concat` are ordinary functions under this same grammar (see
[§11](#11-the-standard-library-so-far)) — there's no special
`printStmt`/`concatStmt` grammar rule the way Lox has a dedicated `print`
statement.

**Known ambiguity**: chaining `.method()` directly onto a call that itself
takes an argument binds to the argument, not the outer call — see
[§13](#13-known-limitations).

## 9. The pipe operator

`a |> f` desugars, entirely at parse time, to the call `f(a)` — there's no
separate runtime representation for a pipe at all, it's just sugar for a
call written in the other order. It chains left-associatively:

```
fun square(n) { return n * n; }
5 |> square |> print   # prints 25, equivalent to print(square(5))
```

The right-hand side of `|>` must be a bare function name (a variable
reference) — `a |> 1` is a compile-time parse error, since `1` isn't
something that can be called.

## 10. Classes and objects

```
class Duck {
    init(name) { self.name = name; }
    quack() { print self.name; }
}

var duck = Duck "Waddles"   # calling the class constructs an instance
duck.quack()                 # Waddles
```

- **`self`**, not `this`, is the self-reference keyword.
- A class with no `init` method has arity zero (`Math()`, no arguments).
  A class's construction arity is always derived from its `init` method's
  arity.
- **Fields** (`self.x = ...`) are created by assignment and are always
  freely reassignable — there's no field-declaration syntax and no
  immutability concept for them (see [§13](#13-known-limitations)).
  Accessing an undefined property on an instance is a runtime error.
- **Single inheritance** via `extends`. Unlike `0.1-poc`'s live
  superclass-pointer-chain walk, `0.1`'s VM copies the superclass's whole
  method table into the subclass once, at `extends`-time (`ObjClass`'s
  `methods` map) — behaviorally identical, since Iqalox has no syntax for
  adding methods to a class after it's declared:

```
class A { greet() { print "A"; } }
class B extends A { }
B().greet()   # A -- B has no override, inherits A's
```

- **`super.method()`** calls up to the superclass method, resolved
  **lexically** — i.e. relative to the class the calling method is
  *written in*, not the actual runtime class of the calling instance. A
  three-level hierarchy where only the middle class overrides and calls
  `super` demonstrates this: the grandchild still ends up invoking the
  grandparent's method through the middle class's `super` call, regardless
  of which subclass instance it's called on. Ordinary (non-`super`) method
  calls, in contrast, dispatch dynamically against the receiver's actual
  runtime class — so an inherited method calling `self.method()` correctly
  reaches a subclass's override.
- **Extending a non-class value** (`class B extends NotAClass { }` where
  `NotAClass` isn't a class) is a runtime error, checked when the `class`
  statement executes.
- **New for `0.1`**: a class can reference itself by name from inside its
  own methods — `0.1-poc` had no placeholder-then-patch binding step to
  support this (see [§13](#13-known-limitations) in
  `docs/LANGUAGE-POC.md`); `0.1`'s resolver defines the class's own name
  in scope before resolving its methods, so a method can look itself, or
  another instance of the same class, up by name.
- Printing a class value itself shows `<class Duck>`; printing an instance
  shows `<Duck instance>`.
- **Out of scope for `0.1`** (deferred to `0.2`, per
  `docs/PLAN-0.1-POC.md` decision 5): getters/setters, mixins (`with`),
  and traits (`trait`/`use`). Only plain field access and baseline Lox
  method dispatch exist today.

## 11. The standard library (so far)

`0.1` has exactly two builtin functions, both registered as ordinary
(shadowable, non-keyword) global bindings by the VM before any user
statement runs (`vm/src/natives.hpp`/`.cpp`) — there is no
`printStmt`/`concatStmt`, and no reserved-word status protects their
names:

- **`print value`** — stringifies `value` (see [§3](#3-values-and-types)'s
  stringification rules: `nil` → `nil`, floats drop a trailing `.0`,
  booleans lowercase) and writes it followed by a newline. Returns `nil`.
- **`concat vector`** — stringifies every element of `vector` and joins
  them with no separator, returning the resulting string. This is
  currently the *only* way to build a string from non-string values or to
  join strings together, since `+` doesn't work on strings
  ([§3](#3-values-and-types)). A non-vector argument is a runtime error
  (`Argument to 'concat' must be a vector, got <type>.`).

```
concat ["a", 1, "b"]   # "a1b"
```

Because these are just values, they can be shadowed like any other name —
attempting to `var print = ...`/`var concat = ...` at the **top level**
is a compile-time error (redeclaring an existing global), the same as
redeclaring any other global; shadowing inside a nested scope is fine:

```
fun test() {
    var print = 42   # shadows the builtin inside this function only
    return print
}
```

Everything else commonly found in a language's standard library — string
manipulation beyond `concat`, array/vector manipulation, math beyond the
operators in [§5](#5-operators-and-precedence), I/O, collections beyond
vector literals — is **not implemented yet**; see `ROADMAP.md`'s "Standard
library vision" section for what's planned and roughly when.

## 12. Errors

Iqalox distinguishes two error phases, both of which currently **abort the
whole program** — there is no way for Iqalox source code to catch or
recover from either kind:

- **Compile-time errors** — scan errors (e.g. an unterminated string, an
  invalid escape sequence, an unexpected character), parse errors (e.g. a
  malformed ternary), resolve errors (e.g. reassigning an immutable
  variable, using `super` outside a subclass), and codegen errors (e.g.
  `break` outside a loop). `iqaloxc` reports each as `[line N] Error:
  message` to stderr and exits with status `65`. Compilation never
  reaches the VM at all if any stage reports an error.
- **Runtime errors** — e.g. assigning to an immutable variable was already
  caught above, but wrong arity, dividing by zero, a non-number operand to
  an arithmetic/comparison operator, an undefined variable/property,
  reading a `var mut` before its first assignment, extending a non-class,
  calling a non-callable value. `iqaloxvm` reports the message plus
  `[line N]` to stderr and exits with status `70`, after whatever output
  was already produced. The line comes from a per-instruction-byte line
  table the bytecode format carries alongside the instructions themselves
  (format v2, `compiler/src/Bytecode.fs`) — every emitted instruction is
  stamped with whichever source token `Codegen.fs` most recently had in
  view, the same way `clox` stamps every byte it emits with the parser's
  current line.

**Remaining, smaller gap versus `0.1-poc`**: `0.1-poc` additionally shows
*which token* triggered the error (`Error at 'x': ...`) plus a caret-
underlined source excerpt, for both compile-time and runtime errors. `0.1`
has real line numbers for both now, but not yet the token text or source
excerpt — the VM in particular has no access to the original source text
at all (it only ever receives a compiled `.iqbc` file), so reproducing
`0.1-poc`'s excerpt there would need embedding source text in the bytecode
format, not just a line table. See [§13](#13-known-limitations) item 7.

There is no `try`/`catch`/`throw` construct in the language itself.

## 13. Known limitations

These are documented tradeoffs and open items, not undiscovered bugs — see
`docs/PLAN-0.1.md` and `docs/PLAN-0.1-POC.md` for the full history of how
each was arrived at.

1. **Vectors are literals only.** `[1, 2, 3]` produces a value with no
   indexing, mutation, length, or other operations — full array support is
   `0.2` scope.
2. **No string concatenation via `+`.** `"a" + "b"` is a runtime error
   (arithmetic `+` requires both operands to be numbers); use `concat
   ["a", "b"]` instead.
3. **Object fields have no immutability concept at all.** Unlike `var`,
   `self.x = ...` is always allowed, both the first time and on every
   later reassignment — there's no `mut`-equivalent for fields, and no
   field-declaration syntax to hang one on even if there were.
4. **Chaining `.method()` straight onto a call that itself takes an
   argument is ambiguous, and currently binds to the argument, not the
   outer call.** `B "Bea".greet()` parses as `B(("Bea").greet())`, not
   `(B("Bea")).greet()` — there's no parenthesis marking where a
   paren-free call's argument list ends, unlike Lox's `f(x).y`. Workaround:
   bind the call's result to a variable first, or wrap the whole call in
   its own grouping parens (`(B "Bea").greet()`).
5. **No built-in methods on primitive values.** Numbers, strings,
   booleans, and vectors have no methods of their own (`"hi".length()`
   doesn't exist) — only user-defined class instances support method
   dispatch.
6. **No concurrency, no exception handling, no reflection/metaprogramming**
   exist at the language level (see [§1](#1-introduction-and-classification)).
7. **No token text or source excerpt on errors, unlike `0.1-poc`.**
   `0.1-poc` reports both compile-time and runtime errors as `[line N]
   Error at 'x': message` plus a caret-underlined single-line source
   excerpt. `0.1` reports a real `[line N]` for every error kind
   (including runtime errors, via a per-instruction-byte line table in the
   bytecode format itself — format v2, `compiler/src/Bytecode.fs`), but
   not yet the offending token's text or a source excerpt. This was
   originally a bigger, confirmed regression (no line number *at all* on
   runtime errors or codegen errors) found while writing this document,
   not caught by the Phase 9 conformance suite (which only diffs
   successful-run stdout, not error-path stderr/exit codes) — the line-
   number part is now fixed; the excerpt/token-text part remains open,
   and for the VM specifically would need embedding source text in the
   bytecode format (it only ever receives a compiled `.iqbc` file, never
   the original source), not just a line table.
8. **Leading-underscore identifiers scan correctly here but not in
   `0.1-poc`.** Fixed in `compiler/src/Scanner.fs` (`docs/PLAN-0.1.md`
   Phase 2) but not carried back into `poc/`, since decoupling
   the two implementations' bugfix schedules is the deliberate model going
   forward (see `docs/PLAN-0.1-POC.md`'s running list) — see
   `docs/LANGUAGE-POC.md` §13 for the `0.1-poc`-side version of this
   limitation.
9. **A vector literal or grouping expression can't span multiple lines.**
   `compiler/src/Scanner.fs` turns *every* newline into an implicit
   `;` unconditionally, with no awareness of bracket/paren depth — so
   `print (\n1 + 2\n)` or a `[...]` literal split across lines both fail
   with `Expect expression.` at the line following the opening bracket,
   the newline having already terminated the "statement" right after it.
   Found while writing a `0.2`-target example (`docs/PLAN-0.2.md` Phase
   1), not by any existing test, since every fixture up to now happened
   to keep every bracketed expression on one line. Not fixed here —
   making the scanner bracket-depth-aware is a real scanner change with
   its own test surface, out of scope for the indexing work that
   surfaced it.
10. **A non-identifier callee can't be called at all, with or without
    parentheses.** `CallHead()`'s "am I followed by something that looks
    like an argument" check only ever runs for a bare `IDENTIFIER` (or,
    indirectly, a property/method access via `FinishPropertyAccess`) —
    a grouped expression never gets that treatment, so neither
    `(f) 5` nor even `(f)()` parses (`Expect line break or ';' after
    expression.`); the callee has to be bound to a name first. Pre-existing
    since `0.1`'s original call grammar, not new here, but newly relevant
    once lambdas (`0.2`, `docs/PLAN-0.2.md` Phase 2) make "produce a
    callable value inline" common — an immediately-invoked lambda
    expression isn't currently expressible.

The following `0.1-poc` limitations are **resolved** as of `0.1` (see
`docs/LANGUAGE-POC.md` §13 for their original write-ups): no escape
sequences in string literals (now fixed, §2 above), immutability
enforcement being runtime-only rather than compile-time (now fixed, §4
above), and classes being unable to reference themselves by name (now
fixed, §10 above).

## 14. Grammar and precedence reference

The authoritative, up-to-date grammar lives in
`langspec/SYNTAX_GRAMMAR.md`; the precedence table (same information,
presented tightest-to-loosest) is in the root `README.md`. Both describe
the language independent of any one implementation — refer to those files
directly rather than duplicating them here, since a third copy would only
be one more place for the three to drift out of sync.

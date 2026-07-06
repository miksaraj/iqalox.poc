# The Iqalox Language (0.1-poc)

This is the complete reference for Iqalox as implemented by the `0.1-poc`
tree-walk interpreter in `poc/src/`. It documents what the language
**actually does today**, with runnable examples and an explicit list of
known limitations — it does not speculate about `0.1`, `0.2`, or later
versions except where noted for contrast. See `ROADMAP.md` for what's
planned beyond this milestone, `docs/PLAN-0.1-POC.md` for the day-to-day
`0.1-poc` implementation log and open design questions, and
`docs/PLAN-0.1.md` for the real `0.1` implementation (compiler frontend in
F#, bytecode VM backend in C++23) currently in progress alongside this one.

Every code sample below is valid Iqalox and, unless marked otherwise, runs
successfully against `poc/src/iqalox.py`. Longer, complete programs live
under `langspec/examples/*.iqx`.

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
personal language project. `0.1-poc` is a **tree-walk interpreter written
in Python**, deliberately positioned as a proof of concept ahead of a
future bytecode implementation (see `ROADMAP.md`'s architecture note); the
implementation language for that future version is undecided.

Classifying `0.1-poc` along the axes commonly used to describe programming
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
  surface as runtime errors, not at parse/scan time. Within that, Iqalox
  leans **strong**: operators check operand types and raise
  `IqaloxRuntimeError` rather than silently coercing between them (no
  string-to-number or number-to-string coercion happens for `+`, `-`, `*`,
  `/`, `%`, `^`, or the comparison operators).
- **Memory management**: fully **automatic**, and not actually implemented
  by Iqalox at all — every runtime value is a plain host-language (Python)
  object (`float`, `str`, `bool`, `list`, or an `IqaloxInstance`/
  `IqaloxFunction`/`IqaloxClass`), so allocation and collection are
  entirely delegated to the Python runtime and its garbage collector. There
  is no manual memory management, no pointers, and no language-level
  concept of ownership or lifetimes.
- **Execution model**: a classic **tree-walking interpreter** — source is
  scanned into tokens, parsed directly into an AST (`poc/src/expression.py`/
  `poc/src/statement.py`), and the AST is walked and evaluated node-by-node
  via the visitor pattern (`poc/src/interpreter.py`). There is no bytecode,
  no intermediate representation, and no JIT. (A bytecode-compiled
  implementation is in progress for `0.1` — see `docs/PLAN-0.1.md`.)
- **Evaluation strategy**: **eager (strict)** evaluation everywhere except
  the two short-circuiting logical operators `and`/`or`, which don't
  evaluate their right operand unless needed. Function arguments are fully
  evaluated before a call executes (**call-by-value** for the argument
  slots themselves; since the values passed are host Python object
  references, mutable values like vectors are shared by reference the way
  they would be in Python).
- **Scoping**: **lexical (static) scoping**. Each block, function call, and
  `for` loop iteration creates a new `Environment` chained to the one
  active where it's written in the source, and name lookup walks that
  chain outward. Closures capture the environment active at the point a
  function is *declared*, not where it's *called*. `super` resolution is
  achieved the same way — via an extra environment layer injected at class
  *declaration* time — so it resolves to the lexically enclosing class's
  parent, never to the calling instance's actual runtime class (see
  [§10](#10-classes-and-objects)).
- **Name-binding / mutability default**: **immutable by default**. `var x
  = 1` cannot be reassigned; `var x mut = 1` opts in to reassignment. This
  extends to function parameters (also immutable inside the function body)
  but *not* to object fields, which are always freely reassignable — a
  deliberately asymmetric, explicitly-flagged design point (see
  [§13](#13-known-limitations)).
- **Object model**: **class-based** (not prototype-based), with **single
  inheritance** via `extends` and **nominal** dispatch — a method call
  resolves by walking the instance's class and its superclass chain by
  name, with no structural/duck-typed interface concept. The object model
  is currently **partial**: user-defined classes produce real objects with
  method dispatch, but the built-in value types (numbers, strings,
  booleans, vectors) are plain values with **no methods of their own** —
  there's no `"hello".length()`-style access on primitives in `0.1-poc`.
- **Concurrency model**: **none**. Iqalox is single-threaded with no
  concurrency primitives, no `async`/`await`, and no scheduling model of
  any kind at this stage.
- **Error-handling model**: internally, control flow and errors both use
  host-language exceptions (`BreakSignal`/`ContinueSignal`/`ReturnSignal`/
  `IqaloxRuntimeError`), but **none of this is exposed to Iqalox source
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
  type (`5` and `5.0` are the same value). `stringify` drops a trailing
  `.0` when printing, so `print 5` prints `5`, not `5.0`.
- **Strings** may use single or double quotes interchangeably, but **no
  escape sequences are processed** — a string is everything between the
  matching quote characters, verbatim (see [§13](#13-known-limitations)).
- **Comments**: `# ...` runs to end of line; `<# ... #>` is a block comment
  and may span multiple lines. Neither nests.
- **Implicit semicolons**: a newline is scanned as a `SEMICOLON` token,
  exactly like a literal `;`. A blank line (or a comment-only line) becomes
  a harmless empty statement, not an error.
- **Keywords** (cannot be used as identifiers): `and`, `class`, `false`,
  `fun`, `for`, `nil`, `or`, `return`, `super`, `self`, `true`, `var`,
  `extends`, `break`, `continue`, `mut`. (`with`, `module`, `trait`, `use`
  are reserved for future mixin/trait/module support but have no grammar
  or meaning yet.) `print` and `concat` are **not** keywords — see
  [§11](#11-the-standard-library-so-far).

## 3. Values and types

Iqalox has five kinds of runtime value in `0.1-poc`:

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

**Truthiness**: only `nil` and `false` are falsy. Every other value — `0`,
`""` (empty string), and `[]` (empty vector) included — is truthy:

```
print (0 ? "truthy" : "falsy")     # truthy
print ("" ? "truthy" : "falsy")    # truthy
print (nil ? "truthy" : "falsy")   # falsy
```

**Equality** (`==`/`!=`) compares values directly (via the host language's
own equality) and never raises — comparing a number to a string, for
instance, is a well-defined `false`, not an error. **Ordering** comparisons
(`>`, `>=`, `<`, `<=`) and **arithmetic** (`+`, `-`, `*`, `/`, `%`, `^`)
require both operands to be numbers and raise `IqaloxRuntimeError`
otherwise — there is no string comparison or string `+` concatenation (use
`concat`, [§11](#11-the-standard-library-so-far)).

Vectors are **literals only** in `0.1-poc`: `[1, 2, 3]` produces a value,
but there is no indexing (`v[0]`), no mutation, no `length`, and no other
vector-manipulation stdlib yet — that's `0.2` scope (`ROADMAP.md`).

## 4. Variables and mutability

```
var x = 1          # immutable -- must be initialized
var y mut = 1      # mutable -- may be reassigned
y = 2              # OK
x = 2              # runtime error: assigning to immutable variable 'x'
var z mut          # OK -- mutable declarations may omit the initializer,
                    # z simply starts out nil
```

An immutable (`var x`, no `mut`) declaration **must** have an initializer —
`var x` with no `mut` and no `= ...` is a parse error, since there would be
no way to ever give it a value. A mutable declaration may omit the
initializer, in which case the variable starts as `nil`.

Reassigning a variable that wasn't declared `mut` is a **runtime** error
(`IqaloxRuntimeError`), not a compile-time one — `0.1-poc` has no static
analysis pass to catch this earlier (see [§13](#13-known-limitations)).
Function parameters are immutable inside the function body by the same
rule, with no per-parameter `mut` syntax to opt out.

Object fields (`self.x = ...`) are the one exception: they are **always**
freely reassignable, with no `mut` concept at all (see
[§13](#13-known-limitations) for why).

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
legal ternary branches. Evaluating one raises an internal signal that
propagates up to the nearest enclosing `for` loop; using either outside of
a loop is reported as a runtime error rather than crashing the
interpreter.

`for` is the only loop construct, with the full three-clause C-style form,
every clause optional:

```
for (var i mut = 0; i < 5; ++i) { print i; }
for (;;) { break; }   # infinite loop, only escape is a break somewhere inside
```

The **ignore operator** `_` is a expression that evaluates to `nil` with
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
(mutations to captured `mut` variables are visible across calls):

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
name is bound in its enclosing environment before its body runs):

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
reference) — `a |> 1` is a parse error, since `1` isn't something that can
be called.

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
- **Single inheritance** via `extends`; `IqaloxClass.find_method` walks
  the superclass chain, so a subclass with no override of its own still
  inherits the parent's method:

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
  of which subclass instance it's called on.
- **Extending a non-class value** (`class B extends NotAClass { }` where
  `NotAClass` isn't a class) is a runtime error, checked when the `class`
  statement executes.
- Printing a class value itself shows `<class Duck>`; printing an instance
  shows `<Duck instance>`.
- **Out of scope for `0.1-poc`** (deferred to `0.2`, per
  `docs/PLAN-0.1-POC.md` decision 5): getters/setters, mixins (`with`),
  and traits (`trait`/`use`). Only plain field access and baseline Lox
  method dispatch exist today.

## 11. The standard library (so far)

`0.1-poc` has exactly two builtin functions, both registered as ordinary
(shadowable, non-keyword) bindings in the global environment — there is no
`printStmt`/`concatStmt`, and no reserved-word status protects their
names:

- **`print value`** — stringifies `value` (see [§3](#3-values-and-types)'s
  stringification rules: `nil` → `nil`, floats drop a trailing `.0`,
  booleans lowercase) and writes it followed by a newline. Returns `nil`.
- **`concat vector`** — stringifies every element of `vector` and joins
  them with no separator, returning the resulting string. This is
  currently the *only* way to build a string from non-string values or to
  join strings together, since `+` doesn't work on strings
  ([§3](#3-values-and-types)).

```
concat ["a", 1, "b"]   # "a1b"
```

Because these are just values, they can be shadowed like any other name:

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

- **Scan/parse errors** (`[line N] Error at 'x': ...`) — e.g. an
  unterminated string, an unexpected character, a malformed ternary.
  Running a file with any of these exits with status `65` and never
  reaches the interpreter at all.
- **Runtime errors** (`IqaloxRuntimeError`, printed as the message plus
  `[line N]`) — e.g. assigning to an immutable variable, calling a
  non-callable value, wrong arity, dividing by zero, a non-number operand
  to an arithmetic/comparison operator, an undefined variable or property,
  extending a non-class. Running a file that hits one of these exits with
  status `70`, after whatever output was already produced.

Both kinds also print the offending source line itself, with a `^`
underline pointing at exactly where the error starts (spanning the whole
lexeme, not just its first character):

```
[line 2] Error at '@@@': Unexpected characters: @@@
    var y = @@@ 2
            ^^^
```

A run of consecutive unrecognized characters (`@@@` above) is reported as
**one** error, not one per character.

There is no `try`/`catch`/`throw` construct in the language itself.

## 13. Known limitations

These are documented tradeoffs and open items, not undiscovered bugs — see
`docs/PLAN-0.1-POC.md` for the full history of how each was arrived at.

1. **No escape sequences in string literals.** `"a\nb"` is the four
   literal characters `a`, `\`, `n`, `b` — there is no `\n`, `\t`, `\"`,
   etc. processing in the scanner.
2. **Vectors are literals only.** `[1, 2, 3]` produces a value with no
   indexing, mutation, length, or other operations — full array support is
   `0.2` scope.
3. **No string concatenation via `+`.** `"a" + "b"` is a runtime error
   (arithmetic `+` requires both operands to be numbers); use `concat
   ["a", "b"]` instead.
4. **Immutability enforcement is runtime-only, not compile-time.**
   Reassigning an immutable variable is *ideally* a compile-time error,
   but `0.1-poc` has no static analysis/resolver pass to do that yet (see
   `docs/PLAN-0.1-POC.md` decision 2) — it's caught at the moment the
   assignment would execute instead.
5. **Object fields have no immutability concept at all.** Unlike `var`,
   `self.x = ...` is always allowed, both the first time and on every
   later reassignment — there's no `mut`-equivalent for fields, and no
   field-declaration syntax to hang one on even if there were.
6. **Classes can't reference themselves by name from inside their own
   methods.** There's no placeholder-then-patch binding step in
   `Environment` (unlike jlox's approach in the book) — this has never
   blocked any real example, since methods reference `self`/`super`, never
   their own enclosing class's name, but it is a real gap if that's ever
   needed.
7. **Chaining `.method()` straight onto a call that itself takes an
   argument is ambiguous, and currently binds to the argument, not the
   outer call.** `B "Bea".greet()` parses as `B(("Bea").greet())`, not
   `(B("Bea")).greet()` — there's no parenthesis marking where a
   paren-free call's argument list ends, unlike Lox's `f(x).y`. Workaround:
   bind the call's result to a variable first, or wrap the whole call in
   its own grouping parens (`(B "Bea").greet()`).
8. **No built-in methods on primitive values.** Numbers, strings,
   booleans, and vectors have no methods of their own (`"hi".length()`
   doesn't exist) — only user-defined class instances support method
   dispatch.
9. **No concurrency, no exception handling, no reflection/metaprogramming**
   exist at the language level (see [§1](#1-introduction-and-classification)).
10. **Error location is line/column only, with a single-line source
    excerpt.** There's no multi-line context, no "did you mean," and no
    IDE-style rich diagnostics — just the offending line and a caret.

## 14. Grammar and precedence reference

The authoritative, up-to-date grammar lives in
`langspec/SYNTAX_GRAMMAR.md`; the precedence table (same information,
presented tightest-to-loosest) is in the root `README.md`. Both are kept
in sync with `poc/src/parser.py` as the language evolves — refer to those
files directly rather than duplicating them here, since a third copy would
only be one more place for the three to drift out of sync.

# Iqalox 0.1

The first real, non-proof-of-concept **Iqalox** implementation: a compiler frontend
(F#, `compiler/`) that compiles `.iqx` source to a bytecode file, executed by a
stack-based virtual machine (C++23, `vm/`). `0.1` is, by design, everything `0.1-poc`
(the frozen Python tree-walk interpreter kept at `poc/`) had — same syntax, same
semantics, same example programs — plus four additions: `undef`/must-assign-before-read
for uninitialized variables, string escape sequences, compile-time immutability
enforcement, and classes referencing themselves by name from inside their own methods.

This directory holds the language specification (this README, `SYNTAX_GRAMMAR.md`, and
the runnable examples under `examples/`) for the `0.1` milestone. See `../ROADMAP.md`
for the full version plan, `../docs/PLAN-0.1.md` for the detailed implementation log,
and `../docs/LANGUAGE-0.1.md` for the complete, prose language reference (this file and
`SYNTAX_GRAMMAR.md` are the terser grammar-level counterpart). `../archived/` holds
frozen snapshots of earlier *planning* iterations — historical record, not the current
spec, and not the same thing as this directory's own versioned snapshots (see the
top-level `../README.md` for the distinction).

## `0.1` — implemented

- Arrays: vector literals only (`[1, 2, 3]`) — no indexing, mutation, or stdlib
  methods yet (full array support is `0.2`)
- Block comments (`<# ... #>`) and line comments (`# ...`)
- Implicit semicolons (a newline ends a statement, same as a real `;`)
- `continue` and `break`, usable anywhere an expression is (including as a
  ternary branch), since there's no separate loop-statement machinery for
  them to be special-cased inside
- Prefix increment/decrement (`++x` / `--x`) with standard mutating semantics;
  reassigning an immutable (`mut`-less) target is a **compile-time** error
- `extends` for single inheritance, `super.method()` for calling up to it; a
  class can reference itself by name from inside its own methods
- Chainable ternary (`? :`, plus the elvis short form `?:`) **completely
  replaces** `if`/`else` — there is no `if` statement
- `while` does not exist — `for` is the only loop construct
- **No function call — builtin or user-defined — takes parentheses.** See
  `SYNTAX_GRAMMAR.md`'s notes and `../docs/PLAN-0.1-POC.md` decision 4 for
  the full grammar (grouping parens for compound arguments, `()` as an
  explicit zero-arg marker, comma-separated arguments, fixed arity, no
  currying)
- No string concatenation via `+` (numbers only); use `concat` for strings
- Pipe operator (`|>`)
- Ignore operator (`_`)
- Nullable infix operator (`??`)
- Modulo (`%`) and power (`^`), at the same precedence as `*`/`/`
- Comma operator
- Immutability by default (`var x` is immutable; `var x mut` opts in),
  enforced at **compile time**
- Accessing an uninitialized variable (`var x mut` with no assignment yet)
  is a **runtime error** — an implicit `undef` value, distinct from `nil`,
  not something user code can write or observe directly
- String literals process escape sequences (`\n`, `\t`, `\r`, `\\`, `\'`,
  `\"`, `\0`)
- The self-reference keyword is `self`, not `this`
- `print`/`concat` are ordinary builtin function bindings, not keywords —
  callable, shadowable, passable like any other function value
- Classes: declarations, fields (freely mutable, no `mut` concept for them),
  methods, constructors (`init`), single inheritance, `super` — baseline Lox
  method dispatch, nothing fancier

## Explicitly out of scope for `0.1`

Deferred to `0.2` (see `../ROADMAP.md` and the current top-level `SYNTAX_GRAMMAR.md`):

- Matrix support and the full array-manipulation standard library (list
  comprehensions, indexing, mutation)
- Maps and sets
- Anonymous function literals (lambdas/closures) — distinct from the named
  nested-function closures already supported
- Variadic unpacking (`...`)
- Getters/setters — resolved for `0.2` as `pub`/`mut` visibility and
  mutability modifiers, not custom accessor bodies
- Mixin support (`class A extends B with C`, `A with B`) and trait support
  (`trait A {...}` / `class B { use A }`)

## Breaking changes vs. Lox

- `extends` instead of `<` for inheritance
- Chainable ternary operator (`? :` / `?:`) instead of `if`/`else`
- No string concatenation via `+`; use `concat` (array-manipulation
  standard library methods, once they exist, will offer more)
- No parenthesized function calls anywhere in the grammar

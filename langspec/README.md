# Iqalox Proof-of-Concept

The first iteration of **Iqalox** development. The overarching intention is to address
the most glaring features omitted in the educational toy language that is *Lox*, as
well as steer it more into a direction more amenable to the author's personal
preferences.

This directory holds the language specification (this README, `SYNTAX_GRAMMAR.md`,
and the runnable examples under `examples/`) for the **current** target, `0.1-poc`.
See `../ROADMAP.md` for the full version plan and `../docs/PLAN-0.1-POC.md` for the
detailed, up-to-date status of this milestone. `archived/` holds frozen snapshots of
earlier planning iterations — historical record, not the current spec.

## `0.1-poc` — implemented

- Arrays: vector literals only (`[1, 2, 3]`) — no indexing, mutation, or stdlib
  methods yet (full array support is `0.2`)
- Block comments (`<# ... #>`) and line comments (`# ...`)
- Implicit semicolons (a newline ends a statement, same as a real `;`)
- `continue` and `break`, usable anywhere an expression is (including as a
  ternary branch), since there's no separate loop-statement machinery for
  them to be special-cased inside
- Prefix increment/decrement (`++x` / `--x`) with standard mutating semantics;
  reassigning an immutable (`mut`-less) target is a runtime error
- `extends` for single inheritance, `super.method()` for calling up to it
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
- Immutability by default (`var x` is immutable; `var x mut` opts in)
- The self-reference keyword is `self`, not `this`
- `print`/`concat` are ordinary builtin function bindings, not keywords —
  callable, shadowable, passable like any other function value
- Classes: declarations, fields (freely mutable, no `mut` concept for them),
  methods, constructors (`init`), single inheritance, `super` — baseline Lox
  method dispatch, nothing fancier

## Explicitly out of scope for `0.1-poc`

Deferred to `0.2` (see `../ROADMAP.md`):

- Matrix support and the full array-manipulation standard library (list
  comprehensions, indexing, mutation)
- Maps and sets
- Anonymous function literals (lambdas/closures) — distinct from the named
  nested-function closures already supported
- Variadic unpacking (`...`)
- Getters/setters (forced or optional — still an open question)
- Mixin support (`class A extends B with C`, `A with B`) and trait support
  (`trait A {...}` / `class B { use A }`)

## Breaking changes vs. Lox

- `extends` instead of `<` for inheritance
- Chainable ternary operator (`? :` / `?:`) instead of `if`/`else`
- No string concatenation via `+`; use `concat` (array-manipulation
  standard library methods, once they exist, will offer more)
- No parenthesized function calls anywhere in the grammar

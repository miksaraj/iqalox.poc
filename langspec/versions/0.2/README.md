# Iqalox 0.2

Builds directly on `0.1`'s toolchain — a compiler frontend (F#, `compiler/`) that
compiles `.iqx` source to a bytecode file, executed by a stack-based virtual machine
(C++23, `vm/`). `0.2` is everything `0.1` had — same core language, same execution
model — plus real vector manipulation and a first, deliberately-scoped pass at
multiple-inheritance-style class composition.

This directory holds the language specification (this README, `SYNTAX_GRAMMAR.md`, and
the runnable examples under `examples/`) for the `0.2` milestone. See `../ROADMAP.md`
for the full version plan, `../docs/PLAN-0.2.md` for the detailed implementation log,
and `../docs/LANGUAGE.md` for the complete, prose language reference (this file and
`SYNTAX_GRAMMAR.md` are the terser grammar-level counterpart). `../archived/` holds
frozen snapshots of earlier *planning* iterations — historical record, not the current
spec, and not the same thing as this directory's own versioned snapshots (see the
top-level `../README.md` for the distinction).

## `0.2` — implemented

Everything `0.1` had (see `../versions/0.1/README.md`), plus:

- **Vector indexing** (`v[i]`, get and set) — 0-based, bounds-checked, composes with
  call results and nests for matrix-style access (`grid[i][j]`). Told apart from the
  pre-existing paren-free call syntax purely by whitespace (`v[0]` vs. `f [0]`) — the
  only place whitespace is significant anywhere in the grammar.
- **Lambdas** (`(a, b) -> expr`) — anonymous, single-expression closures, distinct
  from `0.1`'s named nested-function closures.
- **Cons** (`[item | list]`) and **list comprehensions** (`[expr | x <- xs]`,
  single-generator, no guard clause this version).
- **Vector-literal spread** (`[...a, ...b]`).
- **Array-manipulation standard library**: `push`, `pop`, `length`, `reverse`, `map`,
  `filter`, `reduce`, `sort` — flat global functions, not vector methods.
- **Matrix support** — a matrix is a vector of vectors, no new literal syntax — plus a
  dedicated standard library: `transpose`, `multiply`, `add`, `subtract`,
  `elementwise`.
- **Property `pub`/`mut` visibility and mutability** (`var name`, `var name mut`, `var
  name pub`, `var name pub mut`) — every property must now be declared in the class
  body; private and write-once by default.
- **Method `pub`/private visibility** — a method without `pub` is only reachable via
  internal (`self.x`) access, resolved protected-like for subclasses.
- **Mixins** (`with`, Scala-style runtime composition of a real class's members — a
  simplified, non-C3-linearized approximation) and **traits** (`trait`/`use`,
  PHP-style compile-time static copying, zero runtime representation).

## Explicitly out of scope for `0.2`

Deferred to `0.3` (see `../ROADMAP.md` and the current top-level `SYNTAX_GRAMMAR.md`):

- Negative vector indices and slice syntax (`v[a:b]`)
- Multi-generator/guarded list comprehensions
- Module support, and revisiting whether the array-manipulation standard library
  should move under a namespace once it exists
- Disallowing unused variables (compile-time warning this version, error later)
- Compiler optimizations
- Full C3 linearization for `with`-mixins (`../ROADMAP.md`'s "Language feature ideas
  under consideration" — not committed to any version yet)

## Breaking changes vs. `0.1`

- **Object fields must now be declared as properties in the class body.** `0.1`'s
  `self.x = value` sprang a field into existence on first assignment with no
  declaration required at all; `0.2` requires a matching `var x [pub] [mut]`
  declaration first, or it's a compile-time error. This breaking change is why
  cross-implementation conformance testing against `poc/` (still `0.1-poc`-level) was
  retired outright rather than patched around — see `../CLAUDE.md`.
- No breaking *syntax* changes to anything `0.1` already had — every addition above is
  additive to the existing grammar.

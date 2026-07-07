# Iqalox Roadmap

This supersedes the ad-hoc version notes previously kept outside the repo.
It reflects one renumbering decision made when this document was written:

> **`0.2-poc` is retired as a separate proof-of-concept stage.** Its content
> (matrix support, array-manipulation stdlib, list comprehensions, ...) now
> ships as plain **`0.2`** — a real release, not a second PoC. Every version
> that used to come after `0.2-poc` is bumped up by one to make room. The
> `langspec/archived/` snapshots (0.1, 0.2, 0.3) are **not** renumbered —
> they're a historical record of planning-as-it-happened, not documentation
> of the current plan. Use the mapping table below to translate old notes.

## Old → new version mapping

| Old label     | New label     |
|---------------|---------------|
| `0.1-poc`     | `0.1-poc`     (unchanged) |
| `0.1`         | `0.1`         (unchanged) |
| `0.2-poc`     | `0.2`         |
| `0.2`         | `0.3`         |
| `0.3`         | `0.4`         |
| `0.4`         | `0.5`         |
| `0.5`         | `0.6`         |
| `0.6`         | `0.7`         |
| `0.7` / `1.0` | `0.8` / `1.0` |

`1.0` stays the name of the eventual stable release regardless of how many
pre-1.0 minor versions it takes to get there.

## Versions

### 0.1-poc — *done*

The original PoC target: address the most glaring gaps in Lox, in Python, as
a tree-walk interpreter. See `docs/PLAN-0.1-POC.md` for the full
implementation log. Now superseded by `0.1` below as the primary
implementation, but kept in the repo (`poc/`) as a frozen, working reference
— see `CLAUDE.md`.

New/changed vs. Lox:

- Array support (vector literals — kept in 0.1-poc even though the original
  notes marked this for `0.2-poc+`, since basic vector literals already
  exist in the code; the full array-manipulation stdlib, list comprehensions,
  and matrices stay in 0.2)
- Block comments (`<# ... #>`)
- Implicit semicolons
- `continue` and `break` statements — usable as ternary branches (see below)
- Prefix increment/decrement operators (`++` / `--`) with standard
  mutating semantics; reassigning an immutable (`mut`-less) target is an
  error, ideally caught at compile time (see `docs/PLAN-0.1-POC.md`)
- `extends` instead of `<`/`>` for inheritance
- Chainable ternary operator (`? :` / elvis `?:`) **completely replaces**
  `if`/`else` — including for statement-like branches (`continue`, `break`,
  function calls with side effects). There is no `if` statement.
- `while` does not exist — `for` is the only loop construct
- **No function call — builtin or user-defined — takes parentheses.**
  `print x`, `concat [a, b]`, `add5 1`, `math.square 3`, `Duck "Waddles"`,
  `fact (n - 1)`. Parentheses still show up in three narrower, unrelated
  roles: grouping a compound (non-primary) argument expression
  (`fact (n - 1)`), the explicit zero-argument call marker (`count()`,
  `B()` — needed so a bare name can still mean "the function value itself,"
  e.g. `print fact`, `return adder`, `test count`), and multiple arguments
  are comma-separated without an enclosing pair (`ifEqualOr 2, 5`) — the
  comma there is an argument separator, not the general comma operator,
  exactly like the comma operator is already suppressed inside `[...]`
  vector literals. Function *declarations* keep their existing
  `fun f(a, b) { ... }` parameter-list parens; this only changes call sites.
  See `docs/PLAN-0.1-POC.md` for the full grammar sketch.
- No string concatenation via `+` (arrays/strings use array-manipulation
  facilities/`concat` instead)
- Pipe operator (`|>`)
- Ignore operator (`_`)
- Nullable infix operator (`??`)
- Comma operator
- Modulo (`%`) and power (`^`) arithmetic operators, at the same precedence
  level as `*`/`/`
- Immutability by default (`mut` keyword to opt in to mutability)
- The self-reference keyword is `self` (not `this`)

Lox baseline mechanics the examples assume and that 0.1-poc therefore also
covers (not "new" features, but not yet implemented either — see the plan doc):

- `for` loops
- Functions, calls, `return`, and closures
- Logical `and` / `or`
- Classes: declarations, fields, methods, constructors (`init`), single
  inheritance via `extends`, and `super` — i.e. baseline Lox method dispatch,
  nothing fancier (no forced getters/setters, no mixins/traits — see 0.2)

Explicitly **deferred out of 0.1-poc** (see 0.2): mixin support
(`class A extends B with C`, `A with B`) and trait support
(`trait A {...}` / `class B { use A }`) — the PHP-vs-Scala-style
implementation question is real but doesn't need answering yet.

### 0.1 — *done*

The first real implementation — a compiler frontend (F#) plus a bytecode
VM backend (C++23), superseding the `0.1-poc` tree-walk interpreter. See
`docs/PLAN-0.1.md` for the full implementation log (all 10 phases done,
including a cross-implementation conformance suite proving `compiler/`+
`vm/` match `poc/`'s output byte-for-byte on every `langspec/examples/`
fixture). Everything targeted for `0.1-poc`, actually complete and
hardened, plus:

- Accessing an uninitialized variable is a **runtime error** (implicit
  `undef`, not implicit `nil`) — a variable must be explicitly assigned
  something (including `nil`) before it can be read.
- Escape sequences in string literals.
- Immutability enforced at compile time, not runtime-only.
- Classes can reference themselves by name from inside their own methods.

See `docs/LANGUAGE.md` for the full `0.1` language reference, including a
diagnostics-quality regression flagged there (§13) as a candidate for a
follow-up phase rather than something silently left undocumented.

### 0.2 *(formerly `0.2-poc`)* — *active target*

Everything originally marked "push to `0.2-poc+`" in the source planning
notes that didn't get pulled forward into 0.1-poc, plus mixin/trait support
(deferred out of 0.1-poc — see above; bundled here as the natural home for
"class system completeness" work, alongside the other items below that were
already headed for this version). See `docs/PLAN-0.2.md` for the full
plan — design decisions, open questions, and phased sequencing:

- Matrix support (alongside existing vector/array support) — as nested
  vectors plus a dedicated stdlib, not new literal syntax
- Array-manipulation standard library, including indexing (`v[i]`),
  cons (`[item | list]`), and list comprehensions (`[expr | x <- xs]`,
  single-generator for this version)
- Anonymous function literals ("lambdas": `(a, b) -> expr`) — distinct
  from the named nested-function closures already in 0.1-poc
- Variadic unpacking (`...` operator) — vector-literal spread
  (`[...a, ...b]`) only for this version
- Getters and setters replaced by **`pub`/`mut` visibility and mutability
  modifiers**, private and immutable by default (`var name`, `var name
  mut`, `var name pub`, `var name pub mut`) — resolves the private-by-default
  property question raised for the old v0.3 (now 0.4, see below), and
  foreshadows `0.5`'s module support directly (`pub` is expected to apply
  to classes themselves then, defining what a module exports)
- Mixin support (`class A extends B with C`, `A with B`) and trait support
  (`trait A {...}` / `class B { use A }`) — resolved as a split by which
  keyword is used: `trait`/`use` is **PHP-style static copying** (matching
  PHP's own `trait`/`use` keywords), `with`-only mixins are **Scala-style
  dynamic linearization** (matching Scala's own `with` keyword)

### 0.3 *(formerly `0.2`)*

- Disallow unused variables — **compile-time warning**
- Array-manipulation standard library improvements, including negative
  indices and slice syntax (`v[a:b]`) deferred from `0.2`
- Full list comprehensions: multiple comma-separated generators (nested/
  Cartesian iteration) and boolean guards — `0.2` ships a single-generator,
  no-guards slice only (`docs/PLAN-0.2.md`); this is the original
  `[x + y | x <- xs, y <- ys, x not y]`-style sketch's full shape (exact
  guard-expression syntax, e.g. whether `not` is real or just informal
  pseudocode for "not equal," still needs pinning down when this starts)
- First set of compiler optimizations (see below)
- **Revisit whether the comma should be a no-parens multi-argument call's
  separator at all** (`push v, 4`, `map fn, v, initial`), and whether a
  lambda's own parameter list needs one either (`(a, b) -> ...`). Both are
  `0.1-poc`'s original design (`poc/src/parser.py`'s `call_head()`),
  faithfully carried through every phase since — not something that crept
  in during `0.2`. Raised by the repository owner while reviewing Phase
  5's array-stdlib examples; kept as-is for `0.2` (comma stays the
  separator) since dropping it is a real breaking grammar change — a
  whitespace-only argument list needs its own answer for where it ends
  (is `f a b + c` a third argument `b + c`, or `+ c` applied to `f a b`'s
  result?) — touching every existing example and the whole call-argument
  test suite, not a small tweak to make inline while reviewing a doc fix.

### 0.4 *(formerly `0.3`)*

- File I/O standard library
- Other standard library enhancements
- Second set of compiler optimizations

### 0.5 *(formerly `0.4`)*

- Module support — **must also revisit module-scoped mixin/trait
  composition here**: `0.2` only delivers the class-scoped case
  (`class C extends Base with M1, M2`, `class D { use T; }`) since real
  `module` declarations don't exist yet; `docs/PLAN-0.2.md` explicitly
  flagged this as deferred, not dropped
- **Revisit whether `0.2`'s array-manipulation stdlib (`length`, `push`,
  `pop`, `reverse`, `map`, `filter`, `reduce`, `sort` —
  `docs/PLAN-0.2.md` Phase 5) should move under a namespace (`Vector.map`)
  and/or become opt-in via explicit inclusion, now that real module
  support exists to make that meaningful.** Deliberately shipped in `0.2`
  as flat, always-injected globals (`compiler/src/Prelude.fs`, matching
  `print`/`concat`'s existing precedent) rather than guessed toward a
  namespaced/gated shape — building either now would have meant starting
  real module-system design work early, inside a stdlib-functions phase,
  ahead of this very entry. Explicitly not a silent lock-in: raised and
  decided live with the repository owner during Phase 5.
- Disallow unused variables — **compile-time error** (upgraded from warning)
- Trigonometric functions standard library
- Other standard library enhancements
- Third set of compiler optimizations

### 0.6 *(formerly `0.5`)*

- CLI input standard library
- Other standard library enhancements
- Fourth set of compiler optimizations

### 0.7 *(formerly `0.6`)*

- Standard library enhancements
- Fifth set of compiler optimizations

### 0.8 / 1.0 *(formerly `0.7` / `1.0`)*

- Standard library enhancements
- Sixth (and possibly final) set of compiler optimizations

## Language feature ideas under consideration

Raised while planning `0.2` (`docs/PLAN-0.2.md`), deliberately not
committed to a specific version yet:

- **Variadic parameters** (`fun f(...args)`, extra call-site arguments
  collect into a vector) and **call-site spread** (`f(...someVector)`,
  unpacking a vector's elements as separate positional arguments). `0.2`
  ships only vector-*literal* spread (`[...a, ...b]`) — these two are a
  bigger step, since variadic parameters are in real tension with `0.1`'s
  stated design ("a call's arity is fixed by the callee's declaration, not
  inferred from how many arguments happen to be written"). That tension
  needs its own resolution before either lands, not just a version slot.

## Compiler optimization concepts under consideration

To be scheduled across the 0.3–0.8 optimization passes above, roughly in
order of expected impact/ease, but not committed to any specific version yet:

- Constant propagation
- Common subexpression elimination
- Loop-invariant code motion
- Global value numbering
- Strength reduction
- Scalar replacement of aggregates
- Dead code elimination
- Loop unrolling

See also: <https://en.wikipedia.org/wiki/Optimizing_compiler>

## Architecture note for the eventual bytecode implementation

The project moves past tree-walking for `0.1`: a compiler frontend written
in **F#** and a bytecode VM backend written in **C++23** — see
`docs/PLAN-0.1.md` for the full plan. The split — per *Crafting
Interpreters* — is:

- **Frontend** (`compiler/`, F#): scanner, compiler (source → bytecode)
- **Backend** (`vm/`, C++23): VM, chunk (bytecode representation),
  debug tooling

## Standard library vision

Not tied to a specific version above; these are the categories the standard
library is expected to eventually cover, roughly in the order suggested by
the roadmap (I/O and strings early, concurrency/networking much later):

1. **Input/Output** — file and stream I/O, console interaction
2. **String manipulation** — concatenation, search, split, format, regex
3. **Data structures** — arrays, lists, dictionaries/maps, stacks, queues, sets
4. **Mathematical operations** — arithmetic, trigonometry, logarithms, random
   number generation, rounding
5. **Date and time** — dates, times, timestamps, time zones, formatting, parsing
6. **Error handling** — raising/handling exceptions, logging, error reporting
7. **Networking** — HTTP requests, TCP/IP, UDP, sockets, URL parsing
8. **File system operations** — create/delete/copy/move files and directories,
   metadata retrieval
9. **Concurrency and threading** — threads, locks, semaphores, synchronization
   primitives
10. **System interaction** — executing system commands, environment variables,
    OS interaction, subprocesses

Exactly which category lands in which version (beyond what's already
allocated above — I/O, arrays, math, modules, CLI input) is not yet decided.

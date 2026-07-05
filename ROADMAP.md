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

### 0.1-poc — *in progress*

The current PoC target: address the most glaring gaps in Lox, in Python, as a
tree-walk interpreter. See `docs/PLAN-0.1-POC.md` for the detailed, current
status of this milestone (what's implemented, what's open, what design
decisions are still pending).

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

### 0.1

Everything targeted for `0.1-poc`, actually complete and hardened, plus:

- Accessing an uninitialized variable is a **runtime error** (implicit
  `undef`, not implicit `nil`) — a variable must be explicitly assigned
  something (including `nil`) before it can be read.

### 0.2 *(formerly `0.2-poc`)*

Everything originally marked "push to `0.2-poc+`" in the source planning
notes that didn't get pulled forward into 0.1-poc, plus mixin/trait support
(deferred out of 0.1-poc — see above; bundled here as the natural home for
"class system completeness" work, alongside the other items below that were
already headed for this version):

- Matrix support (alongside existing vector/array support)
- Array-manipulation standard library, including list comprehensions
  (`[x + y | x <- xs, y <- ys, x not y]`-style) and Julia-inspired
  single/multi-dimensional array manipulation
- Anonymous function literals (lambdas/closures) — distinct from the named
  nested-function closures already in 0.1-poc
- Variadic unpacking (`...` operator)
- Getters and setters — whether these are *forced* (mandatory encapsulation,
  no direct property access) or optional convenience methods is still an
  open question; related to the private-by-default property question raised
  for the old v0.3 (now 0.4, see below)
- Mixin support (`class A extends B with C`, `A with B`) and trait support
  (PHP-style `trait A {...}` / `class B { use A }`) — implementation
  strategy (PHP-style static mixins, Scala-style dynamic traits, or a mix)
  still needs to be decided when this work starts

### 0.3 *(formerly `0.2`)*

- Disallow unused variables — **compile-time warning**
- Array-manipulation standard library improvements
- First set of compiler optimizations (see below)

### 0.4 *(formerly `0.3`)*

- File I/O standard library
- Other standard library enhancements
- Second set of compiler optimizations

### 0.5 *(formerly `0.4`)*

- Module support
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

Once the project moves past tree-walking (implementation language TBD, see
`CLAUDE.md`), the expected split — again per *Crafting Interpreters* — is:

- **Frontend**: scanner, compiler (source → bytecode)
- **Backend**: VM, chunk (bytecode representation), debug tooling

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

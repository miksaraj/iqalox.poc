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

- Array support (vectors — as literal expressions, not yet the full
  stdlib/matrix story, see 0.2)
- Block comments (`<# ... #>`)
- Implicit semicolons
- `continue` and `break` statements
- Prefix increment/decrement operators (`++` / `--`)
- `extends` instead of `<`/`>` for inheritance
- Chainable ternary operator (`? :` / elvis `?:`) in place of `if`/`else`
- Support for standard methods (on classes — exact scope still open, see plan doc)
- No string concatenation via `+` (arrays/strings use array-manipulation
  facilities instead)
- Pipe operator (`|>`)
- Ignore operator (`_`)
- Nullable infix operator (`??`)
- Mixin support (`class A extends B with C`, `A with B`)
- Trait support (PHP-style: `trait A {...}` / `class B { use A }`) — final
  choice between PHP-style static mixins, Scala-style dynamic traits, or a mix
  is still open
- Comma operator
- Immutability by default (`mut` keyword to opt in to mutability)

### 0.1

Everything targeted for `0.1-poc`, actually complete and hardened, plus:

- Accessing an uninitialized variable is a **runtime error** (implicit
  `undef`, not implicit `nil`) — a variable must be explicitly assigned
  something (including `nil`) before it can be read.

### 0.2 *(formerly `0.2-poc`)*

- Matrix support (alongside existing vector/array support)
- Array-manipulation standard library, including list comprehensions
  (`[x + y | x <- xs, y <- ys, x not y]`-style) and Julia-inspired
  single/multi-dimensional array manipulation

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

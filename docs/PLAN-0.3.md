# Plan: getting to 0.3

This is the `0.3` counterpart to `docs/PLAN-0.2.md`: a living plan for the
next feature set on top of `0.2`'s compiler frontend (F#, `compiler/`) and
bytecode VM backend (C++23, `vm/`). Like the `0.1`/`0.2` plans, this
document tracks resolved decisions, flags open ones rather than guessing
at them (per `CLAUDE.md`), and keeps a running status table and
sequencing checklist as work lands.

`0.1-poc` (`poc/`) is **not** getting a `0.3` either — it stays frozen,
same as it did through `0.2` (`CLAUDE.md`).

## 0. Scope: what "0.3" means here

Per `ROADMAP.md`'s `0.3` entry (formerly `0.2`, bumped by the `0.2-poc`
renumbering):

- Disallow unused variables — compile-time **warning** (an error is a
  later version's job)
- Array-manipulation standard library improvements: negative indices and
  slice syntax (`v[a:b]`), deferred from `0.2`
- Full list comprehensions: multiple comma-separated generators and a
  boolean guard clause — `0.2` shipped single-generator, no-guard only
- **Module support**, brought forward from `0.5` so the `0.4`-onward
  standard library buildout has real namespacing to land in from the
  start, rather than after several phases have already shipped as flat
  globals. Also the trigger for revisiting two things `0.2` explicitly
  deferred pending real module support existing: module-scoped mixin/
  trait composition, and whether `0.2`'s array-manipulation stdlib should
  move under a namespace
- First set of compiler optimizations (from `ROADMAP.md`'s "Compiler
  optimization concepts under consideration" list)

## 1. Design decisions (resolved)

Resolved via two `AskUserQuestion` rounds with the repository owner
before this document's first draft — the second round exists because the
first round's module-system answer ("real multi-file compilation") is
substantially bigger in scope than a single multiple-choice option
conveys, the same situation `0.2`'s Phase 8 (`with`-mixin linearization)
ran into; rather than guess at the mechanics, a dedicated follow-up round
pinned down the actual shape before any of this got written down as a
plan.

1. **List comprehension guards reuse ordinary boolean expressions — and
   the guard clause is introduced by its own `|`, not folded into the
   generator list with a comma.** The repository owner corrected a real
   typo in `ROADMAP.md`'s own historical sketch while answering: the
   original text read `[x + y | x <- xs, y <- ys, x not y]` (comma
   before the guard); the actual intended shape is
   `[x + y | x <- xs, y <- ys | x != y]` — generators comma-separated as
   before, then a **second** `|` introduces an optional guard expression.
   `not` was never meant as a real keyword; it was informal shorthand for
   `!=`, and the guard is just any expression already in the grammar
   (`==`, `!=`, `and`, `or`, `!`, ...). Grammar shape:
   `vectorBody → ... | expression "|" generator ("," generator)* ("|" expression)?`
   where `generator → IDENTIFIER "<-" expression`. No new top-level
   ambiguity versus `0.2`'s cons/comprehension split: the second `|` only
   ever appears after a parse has already committed to the comprehension
   branch (seeing at least one `<-`), which cons parsing never reaches.
   **Not separately asked, resolved by direct consequence**: later
   generators may reference earlier generators' bound names in their own
   source expression (`[p | x <- xs, y <- range x]`) — this falls out for
   free from the natural desugaring (nested loops, outermost generator
   first), not a special case needing its own design decision.
2. **Negative indices need no new grammar at all — only a runtime
   semantics change.** `v[-1]` already parses today as ordinary indexing
   with a unary-minus expression inside `[...]`; `0.2`'s `GetIndex`/
   `SetIndex` simply needs their bounds check extended to accept
   `-length..-1` as valid, translating internally to `length + index`.
   Applies to **both** reads and writes (`v[-1] = x` works, same as a
   positive in-range write does today). An out-of-range negative index
   (`v[-100]` on a 3-element vector) is the same "Vector index N out of
   range" runtime error `0.2` already raises, just checked against the
   wider valid range.
3. **Slices are `v[a:b]`, end-**inclusive**, with a dedicated new grammar
   production** (this is genuinely new syntax, unlike negative indexing):
   `slice → "[" expression? ":" expression? "]"`, replacing the plain
   `"[" expression "]"` alternative when a top-level `:` is found before
   the closing `]`. Either bound may be omitted (`v[:3]`, `v[2:]`, `v[:]`
   all valid, defaulting to start/end respectively); either bound may be
   negative, using the exact same from-the-end convention as decision 2.
   A slice always returns a **new** vector (copy semantics, never a
   view) — mutating the result never mutates the source. **No
   slice-assignment in `0.3`** (`v[a:b] = ...` is not valid syntax);
   slices are read-only this version. **Proposed, not separately
   confirmed**: `a > b` after resolving negative bounds (e.g. `v[3:1]`)
   produces an **empty vector**, not a runtime error — consistent with
   how most languages treat a degenerate slice range, and avoids turning
   a common edge case into an error the way an out-of-range single index
   still is. Flag if an error was actually wanted here instead.
4. **Module identity is implicit from file path, not a declared
   header or explicit block.** A file's module name is derived from its
   path relative to the compiled program's root (e.g. `math/vector.iqx`
   → module `math.vector`) — no `module Foo { ... }` block syntax and no
   `module math.vector;`-style header line. Simplest mental model
   (matches Python's own file-is-a-module convention), and needs no new
   top-level declaration grammar at all — every top-level `pub`
   declaration in a file is automatically that file's module's exported
   surface (decision 6 below).
5. **Cross-module references use an explicit `import` statement that
   brings specific names into unqualified scope — a new keyword.**
   `import math.vector.transpose` (dotted module path, then the specific
   member being imported), matching decision 9's *original* sketch
   wording. Multiple imports from possibly-different modules
   comma-separate, mirroring `0.2`'s existing `use A, B` trait-composition
   syntax rather than inventing a brace-grouping form:
   `import math.vector.transpose, math.matrix.multiply`. **Proposed, not
   separately confirmed**: importing a name that collides with an
   existing local/global declaration in the importing file is a
   compile-time "already declared" error, the same category `0.2`
   already raises for redeclaring `print`/a native/a trait/etc. — no
   silent shadowing.
6. **`pub` now also gates module-level visibility, not just
   class-member visibility.** A top-level `fun`/`var`/`class`/`trait`
   declared `pub` is importable from another file; one declared without
   `pub` is module-private (usable anywhere within its own file, but not
   `import`able from anywhere else) — directly foreshadowed by `0.2`'s
   own decision 8. **Necessary consequence, not separately asked about**:
   this makes every existing top-level declaration in every current
   `langspec/examples/*.iqx` fixture implicitly module-private under the
   new rule, same category of breaking change `0.2`'s decision 11 (private
   methods by default) already was — each single-file example still works
   unchanged as long as nothing outside that file needs to `import` from
   it, which is true of every existing fixture today.
7. **Module resolution happens entirely at compile time; `vm/` needs zero
   changes for module support itself.** `iqaloxc` still emits exactly
   **one** `.iqbc` file — `import` resolution, module-path-to-file
   lookup, and `pub`-as-export enforcement all happen inside `compiler/`
   (extending the same textual-merge-then-resolve-together model
   `Program.fs` already uses for `Prelude.fs`, now generalized to a whole
   dependency graph of user files instead of one prelude module). No
   module/symbol-table representation is added to the bytecode format;
   the VM never learns a program came from more than one source file.
   Real separate compilation (recompiling one module without recompiling
   everything that imports it) is explicitly **not** a `0.3` capability —
   logged on `ROADMAP.md` as a future idea if it's ever wanted.
8. **`iqaloxc`'s CLI keeps its current single-entry-point shape — no
   manifest format, no multi-file command line.** `iqaloxc entry.iqx
   out.iqbc` is unchanged; the compiler follows `entry.iqx`'s `import`
   statements to find and parse whatever other files the program
   transitively depends on, resolving file paths relative to `entry.iqx`'s
   own directory (exact multi-root/search-path rules, if ever needed, are
   out of scope until a real use case demands them). No new project/
   package manifest format is introduced this version.
9. **Module-scoped mixin/trait composition needs no new design work —
   it falls out of decisions 4-8 for free.** `0.2`'s open question about
   this (`ROADMAP.md`'s `0.3` entry) assumed real `module` declarations
   might need their own trait/mixin composition rules; under the design
   above, a trait or class is just an ordinary top-level declaration like
   any other, importable via decision 5 the same way a function is —
   `use`/`with` then compose it exactly as `0.2` already does, with no
   awareness that the composed name came from another file at all.

## 2. Open questions (flagged, not decided)

1. **Whether `0.2`'s array-manipulation standard library (`push`, `pop`,
   `length`, `reverse`, `map`, `filter`, `reduce`, `sort`) should move
   under a real module namespace now that one exists, and/or become
   `import`-gated instead of always-injected globals.** `ROADMAP.md`'s
   `0.3` entry explicitly named this as a live decision to make once
   module support's actual shape was known, not something to guess at
   alongside the module-system decisions above — raise this with the
   repository owner specifically when this phase of the sequencing below
   starts, the same way `0.2`'s Phase 8 mixin/trait questions were asked
   right before that phase began rather than during initial `0.2`
   planning.
2. **Exactly which optimizations make up the "first set."**
   `ROADMAP.md`'s "Compiler optimization concepts under consideration"
   list is roughly ordered by expected impact/ease (constant propagation,
   common subexpression elimination, loop-invariant code motion, global
   value numbering, strength reduction, scalar replacement of aggregates,
   dead code elimination, loop unrolling) but not committed to specific
   versions. This is sequencing/scoping, not a language-design question
   (`CLAUDE.md`: engineering decisions don't need sign-off) — §5 below
   proposes constant propagation plus dead code elimination as `0.3`'s
   first set (a natural pairing: DCE finds meaningfully more to remove
   once constants have propagated), flagged here in case a different
   pairing is actually wanted.
3. **Whether `_`-prefixed identifiers suppress the new unused-variable
   warning**, a common convention in languages with similar lints (Rust,
   Go tooling, etc.). Not asked as part of the main design-decision
   rounds since it's a small, reversible detail rather than an
   architectural one — proposed default in §5's relevant phase: yes,
   `_`-prefixed locals/parameters/properties are exempt, mirroring the
   existing `_` ignore-operator's "explicitly don't care about this"
   convention already in the language ([§6](#6-control-flow) of
   `docs/LANGUAGE.md`). Flag if a different convention (or none at all)
   was actually wanted.

## 3. Grammar and architecture additions (overview)

- **Slices**: one new grammar production (decision 3) replacing plain
  `"[" expression "]"` when a top-level `:` appears — `compiler/src/Ast.fs`
  gains a `Slice`/`IndexSet`-adjacent node, `Parser.fs` gains the
  lookahead, `Bytecode.fs`/`vm/` gain a new opcode (`GetSlice`, no
  `SetSlice` per decision 3's read-only rule) rather than overloading
  `GetIndex`.
- **Comprehension guards**: extends `0.2`'s existing `Cons`/
  `ListComprehension` desugaring machinery (`Resolver.fs`) — the
  generator-loop-in-a-synthetic-closure shape gains an extra conditional
  (the guard expression) before the `VectorAppend`, and multiple
  generators desugar to nested loops, outermost first. No new opcodes
  beyond what `0.2`'s `VectorLength`/`VectorAppend` already provide.
- **Unused-variable warnings**: a `Resolver.fs`-only change — track, per
  scope, whether each declared local/parameter/private-property/
  private-method is ever referenced after its declaration; emit a
  warning (not an error — compilation still succeeds, `iqaloxc`'s exit
  status is unaffected) for anything that isn't. No grammar changes, no
  `vm/` changes.
- **Modules**: the largest addition. `compiler/src/Program.fs` grows a
  real dependency-resolution pass — given an entry file, parse it,
  collect its `import` statements, resolve each imported module path to
  a file on disk, recursively repeat for each newly-discovered file
  (cycle detection needed, the same category of problem `0.2`'s
  `flattenTrait` already solved for trait `use`-chains), then feed the
  whole file set through `Resolver.fs` together — extending the existing
  prelude-merge model rather than replacing it. `Resolver.fs` gains
  module-qualified name resolution (mapping `import`ed names to their
  origin file's bindings) and the `pub`-as-module-export check (decision
  6). No `vm/` or bytecode-format changes at all (decision 7).
- **Compiler optimizations**: live entirely inside `Codegen.fs` (or a new
  optimization pass between `Resolver.fs` and `Codegen.fs`, exact
  placement a Phase 7 implementation detail) — no grammar, `Bound.fs`, or
  `vm/` changes; the emitted bytecode's *behavior* must be unchanged,
  only its shape/size/instruction count.

## 4. Feature checklist (parity target)

Every row starts "not started" — ticked off as `compiler/`+`vm/` land
each one, verified via new tests (§6).

| Feature | Design decision(s) | Status |
|---|---|---|
| Negative vector indices (get/set) | §1.2 | Done |
| Slices (`v[a:b]`, end-inclusive, read-only) | §1.3 | Done |
| Multi-generator list comprehensions | §1.1 | Not started |
| List comprehension guard clause | §1.1 | Not started |
| Unused-variable compile-time warning | §2.3 | Not started |
| Module support (file-based, `import`, compile-time-only) | §1.4-9 | Not started |
| Array-manipulation stdlib namespace revisit | §2.1 | Not started (open question) |
| First set of compiler optimizations | §2.2 | Not started |

## 5. Suggested sequencing

A proposed order, not a commitment — reorder freely. Self-contained,
lower-risk items first; modules (the largest, most novel piece of
toolchain work) placed after them so the sequencing checklist has
working, shippable progress even if module support's own scope grows
mid-phase the way `0.2`'s Phase 8 did.

**Phase 0 — `langspec/` versioning move.** *Done.* Moved `0.2`'s
`langspec/` snapshot (grammar docs + examples) into
`langspec/versions/0.2/`, mirroring `docs/PLAN-0.2.md`'s own Phase 0.
Unlike that Phase 0, no CI repointing was needed — the cross-
implementation conformance job that `0.1`→`0.2`'s Phase 0 had to repoint
was already retired outright during `0.2` Phase 7; the one CI job that
still names a `langspec/versions/<n>/examples/` path (`poc/`'s own smoke
run, `.github/workflows/ci.yml`) is intentionally pinned to
`versions/0.1/` specifically (the newest fixture set `poc/` — frozen at
`0.1-poc`-equivalent semantics — can actually still run), not the newest
frozen snapshot in general, so it's correctly left untouched.

**Phase 1 — Negative indices and slices — done.** `vm/src/vm.cpp`'s
`checkVectorIndex` now translates a negative index to `length + index`
before bounds-checking (decision 2) — no grammar change was needed for
this half at all, since `v[-1]` already parsed as ordinary indexing over
a unary-minus expression; only the runtime semantics changed, and the
error message dropped "non-negative" from its wording accordingly (still
a hard error either direction out of range). Slices (`v[a:b]`, decision
3) needed a real new `Ast.Slice`/`Bound.BSlice` node, one new `GetSlice`
opcode (no `SetSlice` — slices are read-only), and a `Parser.fs`
lookahead (`isSliceAhead`) that scans for a bracket-depth-0 `:` before
the closing `]`, explicitly tracking (and skipping past) any ternary
`? :` pair at that same depth so `v[cond ? a : b]` still parses as
ordinary indexing rather than being mistaken for a slice. `Codegen.fs`
emits a plain `Nil` instruction for whichever bound is omitted
(`v[:3]`/`v[2:]`/`v[:]`) rather than giving `GetSlice` its own
optional-operand encoding; `Vm::getSlice` resolves `nil` bounds to
0/`length-1`, translates negative bounds the same way single-index access
does, then **clamps** (rather than erroring) any bound that's still out
of range and returns an **empty vector** rather than erroring when
`start` resolves after `stop` — the specific clamping/empty-on-inverted
behavior proposed in decision 3 and not separately re-confirmed.
`langspec/examples/indexing.iqx` was extended with both features end to
end (verified via the real `compiler/`+`vm/` toolchain), and
`langspec/SYNTAX_GRAMMAR.md`/`docs/LANGUAGE.md` §9 both cover the new
syntax/semantics. New tests: 11 xUnit (3 Parser disambiguation/negative-
index/omitted-bound cases, 2 Resolver, 2 Codegen instruction-sequence,
plus the `Slice` pattern threaded through existing shape-assertion
tests) and 13 Catch2 (negative get/set/out-of-range, and a full slice
matrix: basic, omitted bounds, both omitted, negative bounds, inverted
range, out-of-range clamp, copy-not-view, non-vector receiver, non-number
bound).

**Phase 2 — Full list comprehensions.** Multi-generator (nested-loop
desugaring) and guard-clause (decision 1) support in `Resolver.fs`'s
existing comprehension desugaring path.

**Phase 3 — Unused-variable warnings.** `Resolver.fs`-only (§3); resolve
open question 3 (`_`-prefix exemption) when this phase actually starts.

**Phase 4 — Module system: multi-file compilation infrastructure.**
`Program.fs`'s dependency-resolution pass (file discovery, path-to-module
mapping, cycle detection); `Resolver.fs` processes the whole resolved
file set together. No `import` *statement* yet at this point — just the
plumbing to compile more than one file into one program at all.

**Phase 5 — Module system: `import` and `pub`-as-export.** The `import`
statement itself (decision 5), module-qualified name resolution, and
enforcing decision 6's export rule (a non-`pub` top-level declaration is
invisible outside its own file). This is also where every
`langspec/examples/*.iqx` fixture gets audited for the new default-
private-at-module-level rule, the same kind of audit `0.2`'s Phase 7 did
for method visibility.

**Phase 6 — Revisit: array-manipulation stdlib under a namespace.**
Blocked on open question 1 — raise it with the repository owner once
Phase 5 has landed and real module semantics exist to make the question
concrete, rather than guessing now.

**Phase 7 — First set of compiler optimizations.** Proposed: constant
propagation plus dead code elimination (open question 2). Needs its own
correctness-focused test strategy (§6) beyond the usual instruction-
sequence assertions, since the goal is identical *behavior* via smaller/
faster bytecode, not new behavior.

**Phase 8 — Docs.** Fork `docs/LANGUAGE.md` into `docs/LANGUAGE-0.2.md`
(frozen) plus a new, current `docs/LANGUAGE.md` for `0.3`; verify every
top-level `langspec/examples/*.iqx` fixture end to end; `ROADMAP.md`
marks `0.3` delivered and moves the active-target goalposts to `0.4`.

## 6. Testing strategy

Same split `0.1`/`0.2` established: xUnit (`compiler/tests/`) for scanner/
parser/resolver/codegen-level correctness (the disassembler remains the
primary way to verify codegen output without needing the C++ VM), Catch2
(`vm/tests/`) for VM-level execution and runtime-error behavior, and a
manual, per-phase sweep of `langspec/examples/*.iqx` through the real
`compiler/`+`vm/` toolchain (no automated cross-implementation conformance
suite — retired during `0.2` Phase 7, `docs/PLAN-0.2.md`). Phase 4/5
(modules) additionally need multi-file fixture tests — a new
`langspec/examples/`-adjacent fixture layout (or a dedicated test-only
directory under `compiler/tests/`) for programs that span more than one
`.iqx` file, since every existing fixture is single-file by construction.
Phase 7 (optimizations) additionally needs before/after behavioral
equivalence tests — same program, same observable output, verified
across the optimization pass being on par with the same program compiled
without it — not just new disassembler instruction-sequence assertions.

## 7. Risks

- **Module support is, by a wide margin, the largest single-version
  toolchain change since `0.1`'s original F#/C++ split.** Even with `vm/`
  entirely out of scope (decision 7) and the CLI shape unchanged
  (decision 8), `Program.fs` gains real dependency-graph resolution for
  the first time — cycle detection, multi-file error attribution (an
  error in an imported file needs to report *that* file's own line
  number and path, not just a line number the way `docs/LANGUAGE.md` §15
  item 12 already flags as a gap even for the single-extra-file
  `Prelude.fs` case today), and a meaningfully bigger `Resolver.fs`. If
  Phase 4/5's actual scope turns out larger than this document's
  estimate once implementation starts, the same discovery mid-phase
  `0.2`'s Phase 8 (`with`-mixin C3 scope-check) and Phase 7 (conformance-
  suite retirement) both had, raise it the same way: a direct, concrete
  `AskUserQuestion` laying out the real cost, not a silent scope
  expansion.
- **The multi-file error-attribution gap above isn't new, just wider.**
  `docs/LANGUAGE.md` §15 item 12 already documents that a runtime error
  from inside `Prelude.fs`'s own functions reports a line number relative
  to *that* file, not the user's — a real, disclosed, not-yet-fixed
  limitation. Modules multiply the number of files a compiled program can
  span from two (user file + prelude) to arbitrarily many; whether this
  phase is the right time to finally solve real multi-file source-
  position tracking, versus continuing to log it as a known limitation,
  is worth a direct check-in once Phase 4 is under way rather than
  assuming either answer.
- **Slice grammar (`v[a:b]`) is new punctuation-in-brackets syntax**, the
  first since `0.2`'s indexing/spread/cons work. Low risk on its own (no
  whitespace-adjacency-style ambiguity expected, unlike `0.2` decision
  6's indexing-vs-call collision), but worth a real disambiguation check
  against existing bracket forms (plain indexing, cons, comprehension)
  during Phase 1 implementation rather than assumed clean from this
  document's grammar sketch alone — `0.2`'s own experience (the
  indexing-vs-call collision wasn't caught until Phase 1 implementation,
  not this kind of planning document) is exactly why.

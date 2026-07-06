# Plan: getting to 0.1 (the bytecode compiler)

This is the `0.1` counterpart to `docs/PLAN-0.1-POC.md`: a living plan for
the *real* implementation — a compiler frontend plus a bytecode VM backend —
that supersedes the Python tree-walk interpreter once it's done. Like the
0.1-poc plan, this document tracks resolved decisions, flags open ones
rather than guessing at them (per `CLAUDE.md`), and keeps a running status
table and sequencing checklist as work lands.

`0.1-poc` is **not being thrown away**. It stays in the repo (relocated per
Phase 0 below) as a working reference implementation and as the thing that
proved out the language's grammar and semantics in the first place —
`langspec/examples/*.iqx` and `langspec/SYNTAX_GRAMMAR.md` describe the
*language*, not any one implementation, and both `poc/` and the new `0.1`
stack are expected to stay conformant with them.

## 0. Scope: what "0.1" means here

Per `ROADMAP.md`, `0.1` is "everything targeted for `0.1-poc`, actually
complete and hardened, plus":

- Accessing an uninitialized variable is a **runtime error** (an implicit
  `undef` value, not implicit `nil`) — a variable must be explicitly
  assigned something (including `nil`) before it can be read.

Confirmed in this planning round, `0.1` additionally includes:

- **Escape sequences in string literals** (`\n`, `\t`, etc.) — `0.1-poc`
  has none at all (`docs/LANGUAGE.md` §13, limitation 1).
- **Immutability enforced at compile time**, not runtime-only — `0.1-poc`
  deliberately shipped the runtime-only version as an interim tradeoff
  (`docs/PLAN-0.1-POC.md` decision 2; `docs/LANGUAGE.md` §13, limitation 4).
- **Classes can reference themselves by name from inside their own
  methods** — a real Lox capability that `0.1-poc` couldn't cleanly support
  with its plain-`Environment` binding model (`docs/LANGUAGE.md` §13,
  limitation 6). This restores existing Lox behavior; it isn't a new
  language feature being invented here.

Everything else already implemented for `0.1-poc` (see that plan's §2
status table) needs to reach parity in the new stack — no `0.1-poc` feature
is being dropped.

## 1. Design decisions (resolved)

Resolved in this planning round:

1. **Implementation split: frontend in F#, backend (VM) in C++23.** This
   finally answers `CLAUDE.md`'s previously-open "the implementation
   language for the real, non-PoC releases has not been decided" — `CLAUDE.md`
   needs a Phase 0 update to reflect this (see §4).
2. **Frontend and backend are decoupled through a bytecode file**, not a
   shared in-process call — see §3. Two different language runtimes (.NET
   for F#, native for C++) have no natural shared-process story, so the
   frontend is a standalone compiler CLI that emits a bytecode file, and
   the backend is a standalone VM CLI that loads and executes one. This
   also means each side is independently testable (§7) and can make
   progress before the other side exists.
3. **`langspec/` and `docs/`/`ROADMAP.md` stay at the repository root.**
   They describe the language and the overall project plan, not any one
   implementation — both `poc/` and the new `0.1` stack must stay
   conformant with them.
4. **Phase 0 is a repository reorganization**, done before any bytecode
   work starts (see §4): move `0.1-poc`'s implementation-specific code and
   config into a new `poc/` directory, leaving the root for
   implementation-agnostic material (language spec, planning docs,
   project-wide config) plus the new `frontend/`/`backend/` trees as they
   come online.

## 2. Open questions (flagged, not decided)

Per `CLAUDE.md`: these change observable language behavior or lock in a
substantial, hard-to-reverse architectural commitment, so they're listed
here rather than guessed at. I'm asking about these directly (see the
message accompanying this plan) rather than silently picking defaults.

1. **Exact escape-sequence set, and what happens on an unrecognized one.**
   Candidates: the common C-family set `\n \t \r \\ \' \" \0`; more exotic
   options some languages add on top include `\a`/`\b`/`\f`/`\v` (legacy
   control characters, rarely used), `\xHH` (raw byte/hex escape), and
   `\uXXXX`/`\U........` (Unicode code point escapes). Separately: is an
   unrecognized escape like `\q` a hard compile error (Rust/Swift-style,
   stricter) or does it degrade to a literal backslash+character
   (Python-in-non-raw-string-style, more permissive)? **Recommendation:**
   the common set (`\n \t \r \\ \' \" \0`) plus a hard error on anything
   else — matches the project's existing "operators raise rather than
   silently coerce" strong-typing bias (`docs/LANGUAGE.md` §1).
2. **Does the `undef`-before-read rule apply to object fields, or only to
   `var`-declared bindings?** `ROADMAP.md`'s wording is phrased in terms of
   "a variable." `0.1-poc` fields have no declaration step at all — they
   spring into existence on first `self.x = ...` write, and can be read
   any time after that (`docs/LANGUAGE.md` §10/§13 limitation 5). Extending
   `undef` to fields would mean fields need *some* pre-declaration point
   (inside `init`, or a class-body field-declaration syntax) for
   "assigned yet or not" to even be meaningful — a real design surface,
   not just an implementation detail, and it interacts directly with open
   question 4 below (self-referencing classes) and with the field-
   immutability question `0.1-poc` already flagged and left open
   (`docs/PLAN-0.1-POC.md` §1). **Recommendation:** scope `undef` to
   `var`-declared bindings only for `0.1`, and leave field pre-declaration/
   immutability as a `0.2`-or-later class-system-completeness question
   (it's already adjacent to the getters/setters/mixins/traits work
   already deferred there).
3. **Memory management strategy for the C++ backend.** `0.1-poc` made zero
   decisions here — Python's own GC handled everything. Hand-writing a C++
   VM means picking one: **reference counting** (simplest to start, but
   cycles among closures/instances leak unless a cycle collector is added
   later); a **tracing garbage collector** (mark-sweep, `clox`'s own choice
   in *Crafting Interpreters* Part III — more upfront engineering, but
   handles cycles for free and is the architecture this whole project has
   otherwise tracked closely); or an **arena/region scheme** (simplest of
   all, but only really works if the whole program's memory can be freed
   as one block at process exit — too limited if a REPL or long-running
   process ever matters). **Recommendation:** mark-sweep GC, matching
   `clox` — but this is a substantial, hard-to-reverse commitment on
   performance/complexity tradeoffs, worth an explicit decision rather
   than a quiet default.
4. **Directory naming: `frontend/`/`backend/` vs. something else** (e.g.
   `compiler/`/`vm/`). `frontend/`/`backend/` is what I've used throughout
   this plan since that's the terminology `ROADMAP.md`'s own "Architecture
   note for the eventual bytecode implementation" section already uses —
   flagging in case a different pair of names is preferred before Phase 0
   creates them for real.

Not flagging (engineering calls, not language-design ones, per
`CLAUDE.md`): the bytecode file format itself (§3 sketches a concrete
straw-man), F#/C++ test framework choice (§7), and CI setup (§8) — happy to
revisit any of these too, but they don't change observable language
behavior.

## 3. Target architecture

```
   .iqx source
        |
        v
  +--------------------------------+
  |  frontend/  (F#, .NET)         |
  |  Scanner -> Parser -> AST      |
  |  Resolver (scopes, mutability, |
  |  self-referencing classes)     |
  |  Codegen -> bytecode chunks    |
  +---------------+----------------+
                  | writes a bytecode file (format TBD, see below)
                  v
  +--------------------------------+
  |  backend/  (C++23)             |
  |  Loader -> Chunk/Value model   |
  |  VM (stack-based) + GC         |
  |  Native stdlib (print, ...)    |
  +---------------+----------------+
                  |
                  v
             program output
```

- The frontend is a standalone compiler CLI (working name `iqaloxc`) that
  takes a `.iqx` file and emits a bytecode file.
- The backend is a standalone VM CLI (working name `iqaloxvm`) that takes a
  bytecode file and executes it.
- **Bytecode format v0 straw-man** (to be refined once Phase 1 actually
  needs it): a small binary container — magic number, format version,
  constant pool (numbers/strings), a flat instruction stream per function
  (stack-based opcodes: push-constant, arithmetic/comparison/logical ops,
  jumps, call, return, get/set-local, get/set-global, get/set-property,
  class/method table entries), and a debug-info side table (line/column
  per instruction, reusing the line+column model `0.1-poc` just finished
  hardening — see the two most recent PoC releases). Versioned from day
  one so frontend and backend can evolve independently without silently
  desyncing.
- This split means the frontend's tests can assert against the bytecode it
  produces (a disassembler/pretty-printer, no C++ VM needed yet), and the
  backend's tests can hand-assemble small bytecode fixtures directly (no F#
  frontend needed yet) — each side can make real progress before the other
  exists (see §7).

## 4. Phase 0: repository reorganization

Done first, before any bytecode-specific work, and as a pure move + doc
fixup with **no behavior change** — the relocated `poc/` test suite must
still pass, same test count, immediately after.

**Move** (via `git mv`, preserving history):

- `src/` → `poc/src/`
- `tests/` → `poc/tests/`
- `tools/` → `poc/tools/`
- `pytest.ini` → `poc/pytest.ini`
- `requirements-dev.txt` → `poc/requirements-dev.txt`

**`.gitignore` needs splitting, not a straight move** — its current two
entries are `.idea` (arguably still repo-root-relevant for any future
implementation opened in IntelliJ/Rider) and `__pycache__/`
(Python/poc-specific). Move `__pycache__/` into a new `poc/.gitignore`;
keep a minimal root `.gitignore` for repo-wide concerns; add
`frontend/.gitignore` (`bin/`, `obj/`, ...) and `backend/.gitignore`
(`build/`, `CMakeCache.txt`, `CMakeFiles/`, ...) once those trees exist in
Phase 1.

**Stay at the repository root** (implementation-agnostic): `langspec/`,
`docs/`, `ROADMAP.md`, `README.md`, `CLAUDE.md`, `LICENSE`.

**Docs needing real updates** (describe current/ongoing state, so their
paths must track the move):

- `CLAUDE.md` — the whole "Architecture (current PoC)" section's `src/`/
  `tools/` references, the testing-conventions paragraph's `pytest.ini`/
  `requirements-dev.txt`/`tests/` mentions, and the top-level framing
  ("This repository... holds the first proof-of-concept implementation")
  needs to become "this repo holds multiple implementations: `poc/`
  (0.1-poc, Python, frozen/reference) and `0.1` (`frontend/` + `backend/`,
  in progress)."
- `docs/LANGUAGE.md` — its `src/` path references (e.g. "runs successfully
  against `src/iqalox.py`") become `poc/src/iqalox.py`.

**Docs that stay as-is** (historical record, same principle already
applied to `langspec/archived/`): `docs/PLAN-0.1-POC.md`'s internal
`src/`/`tests/`/`tools/` references describe the implementation *as it was
built* — retroactively rewriting them to say `poc/src/` would misrepresent
the history. Leave them alone; a short note at the top of that file (once
this reorg lands) pointing to the new location is enough.

**Verify after moving:** `cd poc && pytest` still passes (pytest.ini's
`pythonpath = src` is relative to wherever `pytest.ini` lives, so this
should be unaffected by the whole tree moving together as a unit), and
`python3 poc/src/iqalox.py langspec/examples/classes.iqx` still runs clean
from the repo root. `tests/conftest.py`'s own path logic
(`Path(__file__).resolve().parent.parent / 'src'`) is relative to the test
file's own location, so it should need no changes — but confirm with a
real run, not just an inspection, since this is exactly the kind of thing
that looks fine on paper and isn't.

The GitHub repository is literally named `iqalox.poc` — a bigger, separate
conversation, not something this reorg needs to (or should) touch.

## 5. Feature checklist (parity target)

Every row is "not started" — this is a from-scratch reimplementation in a
new stack, not a port of Python code. Ticked off as `frontend/`+`backend/`
together reach each one (verified via the shared `langspec/examples/`
conformance suite, §7).

| Feature | 0.1-poc reference | Status |
|---|---|---|
| Array support (vector literals) | `docs/PLAN-0.1-POC.md` §2 | ⛔ |
| Block comments `<# ... #>` | | ⛔ |
| Implicit semicolons | | ⛔ |
| `continue` / `break` as expressions | | ⛔ |
| Prefix `++`/`--` | | ⛔ |
| `extends` / single inheritance / `super` | | ⛔ |
| Chainable ternary incl. elvis `?:` | | ⛔ |
| `print`/`concat` as ordinary builtin functions | | ⛔ |
| Pipe operator `\|>` | | ⛔ |
| Ignore operator `_` | | ⛔ |
| Null-coalescing `??` | | ⛔ |
| Modulo `%` / power `^` | | ⛔ |
| Comma operator | | ⛔ |
| Immutability by default (`mut`) | | ⛔ (see below: compile-time now) |
| `for` loops | | ⛔ |
| Logical `and`/`or` | | ⛔ |
| Functions, closures, `return` | | ⛔ |
| Classes, `init`, methods, `self` | | ⛔ |
| No-parens paren-free call grammar | | ⛔ |
| Accurate line/column error reporting | 0.1-poc's two post-release fixes | ⛔ (carry forward the design, don't re-discover the bugs) |
| **New for 0.1:** `undef`, must-assign-before-read | `ROADMAP.md` §0.1 | ⛔ |
| **New for 0.1:** string escape sequences | §2 open question 1 | ⛔ |
| **New for 0.1:** compile-time immutability enforcement | `docs/PLAN-0.1-POC.md` decision 2 | ⛔ |
| **New for 0.1:** self-referencing classes | `docs/LANGUAGE.md` §13 limitation 6 | ⛔ |

Known, deliberate `0.1-poc` limitations *not* being fixed as part of this
list unless separately decided: the `.method()`-chaining-onto-an-argument
ambiguity, no built-in methods on primitives, the leading-underscore
identifier scan bug (`docs/LANGUAGE.md` §13) — all still open questions or
low-priority items, not silently resolved here.

## 6. Suggested sequencing

**Phase 1 — Toolchain scaffolding & round-trip proof.** Stand up
`frontend/` as an F# solution and `backend/` as a CMake C++23 project;
define bytecode format v0 (§3) with just enough to represent "call the
`print` native with one string constant"; frontend emits that fixed
bytecode for a hardcoded program, backend loads and executes it. Prove the
whole pipeline end-to-end before any real compiler work starts. Stand up
CI for both toolchains (§8).

**Phase 2 — Scanner (F#).** Port `0.1-poc`'s scanner design — its two
post-release bugfixes (accurate line/column tracking; coalescing runs of
invalid characters into one error) are proven-correct behavior worth
carrying forward directly rather than re-discovering the same bugs — plus
new escape-sequence handling (open question 1).

**Phase 3 — Parser & AST (F#).** AST as F# discriminated unions — a
natural fit, and arguably simpler than `0.1-poc`'s generated-visitor-
pattern approach (`tools/generate_ast.py`) since pattern matching replaces
the visitor dispatch with no code-generation step needed. Port the full
grammar from `langspec/SYNTAX_GRAMMAR.md` (paren-free calls, ternary/elvis,
pipe, comma, null-coalescing, classes) — it's already fully specified.

**Phase 4 — Resolver / semantic analysis (F#).** Compile-time lexical
scope and variable-slot resolution (locals get compile-time-known stack
slots instead of runtime hashmap lookups; closures get upvalues) — the
standard *Crafting Interpreters* Part III resolver, and exactly where
compile-time immutability enforcement and self-referencing-class-name
binding both naturally live:
  - Compile-time immutability check: reject any assignment to an immutable
    binding during this pass, before codegen ever runs.
  - Self-referencing classes: reserve the class's own name slot before
    compiling its method bodies, patch it once the class value is fully
    constructed — adapting jlox's runtime-only placeholder-then-patch
    pattern (that `0.1-poc` couldn't support, `docs/PLAN-0.1-POC.md` §1)
    to a compile-time slot reservation instead.

**Phase 5 — Code generation (F#), bytecode format v1.** Full opcode set
covering every `0.1-poc` expression/statement plus classes/closures.
`undef` becomes a real runtime `Value` case, emitted as the initial value
for any `mut`-without-initializer declaration; reading a slot still holding
it is a runtime error (open question 2 decides whether fields ever
participate in this).

**Phase 6 — VM core (C++23).** Value representation (a tagged
union/`std::variant` to start; NaN-boxing is a later optimization, not a
Phase 6 requirement), stack-based execution loop, chunk/constant-pool
loading matching the frontend's format, and memory management (open
question 3).

**Phase 7 — Native standard library (C++23).** `print`, `concat` at
minimum for parity with `0.1-poc`. Whether any stdlib is ever written in
Iqalox itself (self-hosting) is a separate, later conversation — not
blocking here.

**Phase 8 — Classes & OOP in bytecode form (C++23 + F#).** Bound methods,
single inheritance, `super` calls — `clox`'s approach (superclass method
table resolved substantially at compile time) is the natural fit given the
Phase 4 resolver architecture.

**Phase 9 — Conformance testing against `langspec/examples/`.** Since
those `.iqx` files are language-level, not `0.1-poc`-implementation-
specific, they're natural cross-implementation fixtures: same input, same
expected output, run through both `poc/` and the new
`frontend/`+`backend/` pipeline. Behavioral drift is either an intentional
`0.1-poc` limitation being fixed (expected — note where `0.1` deliberately
diverges) or a real regression worth catching immediately, not something
to let slide.

**Phase 10 — Documentation.** `docs/LANGUAGE.md` either gains a `0.1`
addendum or forks into a versioned doc once `0.1` actually reaches parity
plus the four new items — a late-phase task, done when it's true, not
upfront. `ROADMAP.md` marks `0.1` delivered and moves the active-target
goalposts to `0.2`.

## 7. Testing strategy

- **F# side**: xUnit (the more conventional, lower-setup-cost .NET test
  runner; Expecto is more idiomatic F# but needs more bespoke tooling) —
  swappable later at no real cost, this is pure tooling.
- **C++ side**: Catch2 (lighter-weight, easier to drop into a project this
  size than GoogleTest) — also swappable.
- **Cross-implementation conformance**: a CI job that runs every
  `langspec/examples/*.iqx` through both `poc/` and `frontend/`+`backend/`
  and diffs output (see Phase 9). This is the project's actual regression
  safety net across two otherwise-independent codebases — worth having
  from the moment there's anything to compare, not bolted on at the end.
- Each side is also independently testable without the other existing yet
  (§3) — the frontend against its own disassembled bytecode output, the
  backend against hand-assembled bytecode fixtures.

## 8. Tooling & CI

Checked in this environment as of this planning round:

- **C++23**: CMake 3.28.3, GCC 13.3.0, and Clang 18.1.3 are all present and
  `g++ -std=c++23` is accepted — backend prototyping can start immediately.
- **F#/.NET**: no `dotnet` SDK is currently installed here — needs
  provisioning (in this environment or wherever frontend work actually
  happens) before Phase 1's frontend half can start for real.
- Any new GitHub Actions workflow must SHA-pin every `uses:` step per
  `CLAUDE.md` — this reorg/plan is the project's first real occasion to add
  a workflow at all, worth getting right from the first commit rather than
  retrofitting later.

## 9. Risks

- **Keeping `poc/` and `0.1` in sync where they're supposed to match, while
  deliberately diverging where `0.1` fixes a documented `0.1-poc`
  limitation** — the conformance suite (§7, Phase 9) is the guard rail;
  skipping it would let the two silently drift.
- **Two build toolchains in one repo is real, ongoing maintenance cost** —
  two CI jobs, two dependency ecosystems, contributors need both installed
  for a full build. Worth being clear-eyed about now that the frontend/
  backend language split is locked in, not discovered later.
- **Open question 2 (`undef` and fields) left unresolved for too long**
  could ripple into Phase 8's class design if it's still open when that
  phase starts — worth settling before Phase 4 begins in earnest, not
  after.

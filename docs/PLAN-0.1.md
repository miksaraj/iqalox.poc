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
   project-wide config) plus the new `compiler/`/`vm/` trees as they come
   online.
5. **Escape sequences**: the common C-family set — `\n \t \r \\ \' \" \0` —
   plus a **hard compile error** on any unrecognized escape (e.g. `\q`).
   Matches the language's existing "operators raise rather than silently
   coerce" strong-typing bias (`docs/LANGUAGE.md` §1). No `\xHH`/`\uXXXX`
   escapes for `0.1`.
6. **`undef` scope: `var`-declared bindings only, not object fields.**
   Fields keep `0.1-poc`'s existing model — they spring into existence on
   first `self.x = ...` write, readable any time after
   (`docs/LANGUAGE.md` §13, limitation 5). Field pre-declaration and field
   immutability are deferred to `0.2`'s class-system-completeness work
   (getters/setters/mixins/traits already deferred there), not decided now.
7. **Memory management: a mark-sweep tracing garbage collector**, matching
   `clox`'s own approach in *Crafting Interpreters* Part III — chosen over
   reference counting (cycles among closures/instances would otherwise
   leak) and an arena/region scheme (too limited once a REPL or
   long-running process matters).
8. **Directory names: `compiler/` (F# frontend) and `vm/` (C++23 backend)**
   — more concrete about what each half literally is than the
   `frontend/`/`backend/` working names used while this was still open.
   `ROADMAP.md`'s architecture note and the rest of this plan use these
   names throughout.

## 2. Open questions (flagged, not decided)

None outstanding as of this revision — the four questions raised when this
plan was first drafted (escape sequences, `undef`'s scope, memory
management, directory naming) were all resolved immediately and folded
into §1 above (decisions 5–8). This section is kept, empty, as a living-
document placeholder: per `CLAUDE.md`, anything that changes observable
language behavior or locks in a substantial, hard-to-reverse architectural
commitment gets flagged here rather than guessed at, as soon as it
surfaces during implementation.

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
  |  compiler/  (F#, .NET)         |
  |  Scanner -> Parser -> AST      |
  |  Resolver (scopes, mutability, |
  |  self-referencing classes)     |
  |  Codegen -> bytecode chunks    |
  +---------------+----------------+
                  | writes a bytecode file (format TBD, see below)
                  v
  +--------------------------------+
  |  vm/  (C++23)                  |
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
  desyncing. (Superseded by format v1 once Phase 5 needed a real one —
  see that phase's entry in §6 for the full opcode set and
  `compiler/src/Bytecode.fs`'s own doc comment for the on-disk layout. No
  debug-info side table yet; deferred until something actually consumes
  it.)
- This split means `compiler/`'s tests can assert against the bytecode it
  produces (a disassembler/pretty-printer, no C++ VM needed yet), and
  `vm/`'s tests can hand-assemble small bytecode fixtures directly (no F#
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
`compiler/.gitignore` (`bin/`, `obj/`, ...) and `vm/.gitignore`
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
  (0.1-poc, Python, frozen/reference) and `0.1` (`compiler/` + `vm/`,
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
new stack, not a port of Python code. Ticked off as `compiler/`+`vm/`
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
| **New for 0.1:** `undef`, must-assign-before-read (`var` only, decision 6) | `ROADMAP.md` §0.1 | ⛔ |
| **New for 0.1:** string escape sequences (decision 5) | | ⛔ |
| **New for 0.1:** compile-time immutability enforcement | `docs/PLAN-0.1-POC.md` decision 2 | ⛔ |
| **New for 0.1:** self-referencing classes | `docs/LANGUAGE.md` §13 limitation 6 | ⛔ |

Known, deliberate `0.1-poc` limitations *not* being fixed as part of this
list unless separately decided: the `.method()`-chaining-onto-an-argument
ambiguity, no built-in methods on primitives, the leading-underscore
identifier scan bug (`docs/LANGUAGE.md` §13) — all still open questions or
low-priority items, not silently resolved here.

## 6. Suggested sequencing

~~**Phase 1 — Toolchain scaffolding & round-trip proof.**~~ Done. `vm/`
is a CMake C++23 project (`iqaloxvm`, `src/bytecode.{hpp,cpp}` implementing
the format v0 reader, Catch2 tests in `tests/`). `compiler/` is an F#
solution (`Iqaloxc.sln`; `src/Bytecode.fs` implements the matching writer,
`src/Program.fs` emits a hardcoded "print a greeting" chunk; xUnit tests in
`tests/`). `scripts/phase1-roundtrip-smoke-test.sh` builds both and proves
`iqaloxc`'s output is exactly what `iqaloxvm` can load and execute
end-to-end. CI (`.github/workflows/ci.yml`) runs both toolchains' own test
suites, the round-trip script, and (newly, since this was the project's
first workflow) `poc/`'s existing pytest suite plus every
`langspec/examples/*.iqx` script — all `uses:` steps pinned to a full
commit SHA per `CLAUDE.md`. Targets **.NET 10 / F# 10** (the current LTS
as of this writing — not .NET 8, which this phase briefly and mistakenly
targeted before being corrected). Environment note: this environment
needed `dotnet-sdk-10.0` and `catch2` installed via `apt`; CMake/GCC/Clang
were already present (see §8's original findings).

~~**Phase 2 — Scanner (F#).**~~ Done. `compiler/src/Token.fs` (an
idiomatic `TokenType` discriminated union plus the `Token` record) and
`compiler/src/Scanner.fs` (`Scanner.scanTokens : string -> Token list *
ScanError list`) carry forward `0.1-poc`'s two proven-correct post-release
fixes (accurate line/column tracking; coalescing runs of invalid
characters into one error) and add escape-sequence handling (decision 5:
`\n \t \r \\ \' \" \0`, hard error on anything else).

Writing it carefully (rather than a line-for-line port) surfaced five real
`poc` bugs that were never "proven correct" and so weren't carried
forward — all fixed in `Scanner.fs`, none needing design sign-off since
they're straightforward correctness bugs, not language-design questions:

1. **Decimal number literals never worked in `poc` at all.**
   `number()`'s fractional-part check compares `self.peek()` to a digit
   test that *also* defaults to `self.peek()` — it compares the '.'
   character to itself and never looks at what actually follows it.
   `3.14` scans as `NUMBER("3")`, `DOT`, `NUMBER("14")` in `poc`, silently,
   with no test or example ever having used a decimal literal to catch it.
   Fixed in `Scanner.fs` by checking the character *after* the dot
   (`peekNext`).
2. **A leading-underscore identifier (`_foo`) misscans** as a bare `_`
   (the ignore operator) followed by a separate `foo` identifier, since
   `_` is checked as its own token before the identifier dispatch runs —
   a limitation `docs/LANGUAGE.md` §13 already flagged as known-but-
   deprioritized in `poc`. Fixed by checking whether an alphanumeric
   character follows before deciding `_` is standalone.
3. **The `...` (ellipsis) token under-consumes by one character** — `poc`'s
   generic compound-matching only ever advances one extra character
   regardless of a compound's actual length, so `...` becomes a 2-character
   lexeme. Moot today (`...` has no grammar yet, 0.2 scope) but worth not
   carrying forward. Fixed via a longest-match search that consumes
   exactly the matched candidate's length.
4. **A bare `#>` outside a block comment is silently swallowed** with no
   token or error, an accident of treating comment markers as just another
   entry in the same generic compound-matching table as operators. Fixed
   by giving comments (`#`, `<# ... #>`) their own explicit dispatch.
5. **An unterminated block comment produces no error at all** in `poc`
   (the scan loop just exits at EOF with nothing reported), unlike an
   unterminated string, which already errors correctly. Fixed to report
   one the same way.

See task tracking for a proposed small follow-up patch to `poc` itself for
bug 1 (the only one of these that's a real, user-facing defect in a
*shipped* implementation, `0.1.1-poc`) — not done as part of this phase,
since Phase 2's job is the new `0.1` scanner, not further `poc`
maintenance.

~~**Phase 3 — Parser & AST (F#).**~~ Done. `compiler/src/Ast.fs` defines
`Expr`/`Stmt` as F# discriminated unions (plus a `FunctionDecl` record,
shared between `Stmt.FunctionStmt` and `Stmt.ClassStmt`'s `methods` field
for real type safety `poc`'s `List[Function]` type hint never enforced) —
simpler than `0.1-poc`'s generated-visitor-pattern approach
(`tools/generate_ast.py`), since pattern matching replaces the visitor
dispatch with no code-generation step needed. `compiler/src/Parser.fs`
ports the full grammar from `langspec/SYNTAX_GRAMMAR.md` (paren-free
calls, ternary/elvis, pipe, comma, null-coalescing, classes, vector
literals, `self`/`super`) — a recursive-descent parser matching
`poc/src/parser.py`'s structure closely, with errors collected into a
`ParseError list` (mirroring `Scanner.fs`'s `ScanError` pattern) rather
than reported through `poc`'s global mutable singleton.

Two implementation notes:
- **A real naming collision, not a design question:** `Class`/`Var`/`For`/
  `Return` (natural `Stmt` case names) and `Break`/`Continue`/`Self`/
  `Super` (natural `Expr` case names) are *also* `TokenType` case names
  (`Token.fs`). Both modules are opened together in `Parser.fs`, and F#
  resolves a bare, ambiguous case name to whichever `open` came last —
  silently breaking whichever module lost. Fixed by suffixing the
  colliding `Ast.fs` cases (`ClassStmt`, `VarStmt`, `ForStmt`,
  `ReturnStmt`, `BreakExpr`, `ContinueExpr`, `SelfExpr`, `SuperExpr`);
  every unambiguous case name is left bare.
- **A `poc` bug found while porting `primary()`'s vector-literal handling**,
  fixed here rather than carried forward: `poc` toggles
  `comma_as_operator` off, parses the elements, then toggles it back on —
  but without a `try`/`finally` equivalent, a parse error partway through
  a vector literal's elements (e.g. `[1, 2, bad+]`) leaves the comma
  operator disabled for the *rest of the file*, since the restoring
  assignment never runs after the exception unwinds past it. Fixed with a
  `try`/`finally`. Also fixed in the same pass: `poc`'s `for_statement()`
  assigns `body = self.statement()` with no `None` check, but
  `statement()` returns `None` for a bare `;` body — constructing a `For`
  node whose body is `None`, which crashes the interpreter later. Treated
  as an empty block here instead. Neither needed design sign-off —
  straightforward parser bugs, not language-design questions. See task
  tracking for a proposed `poc` follow-up patch for both, mirroring how
  the scanner bugs were handled.

Regression tests for both in `compiler/tests/ParserTests.fs` (75 F# tests
total, up from 36).

~~**Phase 4 — Resolver / semantic analysis (F#).**~~ Done.
`compiler/src/Bound.fs` defines `BoundExpr`/`BoundStmt` — the same shape
as `Ast.fs`'s `Expr`/`Stmt`, except every variable reference/assignment/
`self`/`super` now carries a `VariableBinding` (`LocalBinding` of a stack
slot, `UpvalueBinding` of an index, or `GlobalBinding` of a name) and
every declaration carries a `DeclaredBinding` (`DeclaredLocal`/
`DeclaredGlobal`) saying where it creates one; each function/method also
gets its resolved `LocalCount` (how many stack slots its frame needs) and
`Upvalues` (how to populate each captured variable when a closure over it
is created). `compiler/src/Resolver.fs`'s `Resolver.resolve` produces this
from the parsed AST, using `clox`'s own scope/slot/upvalue algorithm
(*Crafting Interpreters* Part III) — `poc` has no resolver at all to port
from for this phase, so this one is new, not a port:

- **Compile-time immutability enforcement**: assigning to an immutable
  local, upvalue, or global is now a compile error, not a runtime one.
  Global checks are **order-independent** — a first pass
  (`preRegisterGlobals`) registers every top-level `var`/`fun`/`class`
  name and its mutability *before* any body is resolved, so a function
  referencing a global declared later in the same file (ordinary,
  expected mutual-recursion-style code) still gets a fully-informed
  check, not a guess based on partial information.
- **Self-referencing classes**: a class's own name is declared (as an
  immutable global or local, matching where the `class` statement itself
  lives) *before* its method bodies are resolved, so a method can
  reference its own enclosing class by name — the gap `docs/LANGUAGE.md`
  §13 documented as a `0.1-poc` limitation is closed for `0.1`.
- **`self`/`super`** are resolved through the exact same local/upvalue/
  global machinery as any other name, by looking up the synthetic names
  `"self"`/`"super"` — a method's function context gets an implicit `self`
  local at slot 0, and a class with a superclass gets a synthetic `super`
  local in a scope wrapping all of its methods (mirroring `poc`'s own
  extra-`Environment`-layer trick for `super`, just done at compile time).
  Falling through all the way to an unresolved global for either name is
  exactly the "used outside a method" / "no superclass" error case, since
  neither name can ever be a real user-declared identifier (both are
  reserved keywords).
- **Redeclaration in the same scope** (`var x = 1 ... var x = 2` in the
  same block, or twice at the top level) is now also a compile error,
  matching `poc`'s `Environment.define`'s "already declared" rule, moved
  to compile time.

Not in scope for this phase (no regression versus `poc`, which doesn't
check these statically either): `break`/`continue` outside a loop,
`return` outside a function — these stay dynamically checked, since
adding compile-time tracking for them would need new state
(loop-depth/inside-function counters) that nothing else in this resolver
needs, unlike `self`/`super`, which reuse the exact same machinery already
built for ordinary variable resolution at zero extra cost. (Update, Phase
5: `break`/`continue` outside a loop ended up checked at compile time after
all — `Codegen.fs` already needs its own loop-context stack to patch jump
targets, so the check was effectively free there. `return` outside a
function remains unchecked, same reasoning as before.)

95 F# tests total (up from 75), covering locals/upvalues (including a
doubly-nested closure capturing a grandparent's local through an
upvalue-of-an-upvalue)/globals resolution, both immutability-enforcement
cases, redeclaration, self-referencing classes, and all four `self`/
`super` scoping scenarios.

~~**Phase 5 — Code generation (F#), bytecode format v1.**~~ Done.
`compiler/src/Bytecode.fs` was rewritten wholesale from Phase 1's minimal
v0 straw-man (just enough to prove the round trip) to a real v1 format: a
structured, index-based `Instruction`/`Chunk`/`Constant`/`FunctionProto`
representation that `Codegen` builds and `Disassembler.fs` (new) prints
directly, with jump targets as plain instruction-array indices — the
*serialized* on-disk format (magic/version header, constant pool,
length-prefixed instruction stream) only exists at the very end, in
`Bytecode.write`, which is also where index-based jump targets get
translated to the byte offsets the format actually stores. `vm/` still
only reads v0 — rebuilding it for v1 is Phase 6's job, one phase behind by
design, same relationship v0 had to Phase 1.

`compiler/src/Codegen.fs` (`Codegen.compile : BoundStmt list -> Chunk *
CodegenError list`) covers every `0.1-poc` expression/statement plus
classes/closures/`undef`:

- **No dedicated "store" instruction for a local's declaration.** Its
  initializer's pushed value already sits in the stack slot `Resolver`
  assigned it, since slots are handed out in strict push order; only
  re-assignment needs an explicit, non-popping `SetLocal`/`SetUpvalue`
  (assignment is itself an expression, so its value stays on the stack).
  Globals are the mirror image: not stack-resident, so declaration pops
  via `DefineGlobal` but assignment still doesn't pop.
- **`undef`** is real: a `mut`-without-initializer `var` declaration emits
  the new `Undef` opcode as its initial value instead of `Nil`. (Whether
  reading a slot still holding it is a runtime error is `vm/`'s job,
  Phase 6 — the frontend's only responsibility here is emitting the
  distinct value.)
- **A running `StackDepth` counter** (`Codegen`'s own, separate from
  `Resolver`'s already-computed slot numbers) tracks how many values are
  currently pushed, updated automatically by routing every emission
  through one `Emit` method annotated with each opcode's net stack effect.
  This is what lets block/function exits know how many slots to `PopN`,
  and lets `break`/`continue` unwind the right number of slots inline at
  their own jump site rather than sharing (and double-popping via) one
  common loop-exit cleanup path. The one spot a purely sequential counter
  isn't enough: a `for` loop's condition check and its "falsy, exit now"
  landing point both flow from the same `JumpIfFalse`, but only the
  fallthrough (truthy) path has popped the condition value by the time the
  counter reaches the landing point textually — handled with an explicit
  depth reset at that one merge point instead of assuming the linear walk
  stays accurate.
- **Three more real `poc` bugs**, fixed here rather than carried forward
  (logged in `docs/PLAN-0.1-POC.md`'s running list, per the standing
  policy of fixing correctly in the new stack while only *noting* — not
  patching — `poc` itself): the comma operator always evaluated to `nil`
  in `poc` (no interpreter case for it at all) — fixed with no dedicated
  opcode needed, just `compile left; Pop; compile right`; `??` didn't
  short-circuit in `poc` (both operands always evaluated) — fixed via a
  new peek-based `JumpIfNotNil` opcode; elvis (`?:`) double-evaluated its
  condition in `poc` (the parser reuses one AST node for both the
  ternary's `left` and `middle`, but the interpreter evaluates each
  independently) — fixed by telling elvis apart from a full ternary via
  the operator token (`QuestionMarkColon` vs. `QuestionMark` — `middle`
  can't be used to distinguish them, since `Resolver` re-resolves the
  shared node into two structurally-identical-but-not-reference-equal
  copies) and evaluating the condition exactly once.
- **A real gap surfaced in already-merged Phase 4 code, fixed here**:
  `Bound.BSuper` only carried `super`'s own resolved binding, not `self`'s
  — fine for `super.method()` called directly inside a method (`self` is
  always local slot 0 there), but wrong for a call from inside a closure
  *nested within* a method, where `self` may itself need to be captured as
  an upvalue. `poc`'s dynamic `Environment` chase gets this for free;
  the compile-time slot/upvalue scheme doesn't unless it's tracked
  explicitly. Fixed by having `Resolver.fs`'s `SuperExpr` case resolve
  `self` independently of `super` (mirroring `clox`), and widening
  `BSuper` to carry both bindings — a two-line `Resolver.fs` change plus a
  `ResolverTests.fs` update, not a Codegen-only workaround.
- **Class/method codegen** matches `clox`'s own approach: `Class`, then
  (if the binding is global) `DefineGlobal`, then the superclass value if
  any (this push *is* the synthetic `super` local `Resolver` already
  allocates immediately next — no `Bound.fs` change needed to make the
  slot numbers line up), then a temporary re-fetch of the class value so
  each compiled method's trailing `Method` instruction can peek "the
  class" at a fixed relative stack position regardless of what's
  underneath, `Inherit` if there's a superclass, one `Closure`+`Method`
  pair per method, and a final `Pop` discarding the temporary re-fetch
  (not the class itself).
- **`for` loop `break`/`continue`** unwind to two different depths —
  `break` all the way to before the loop's own initializer (the for
  statement's whole scope is exiting), `continue` only to just after the
  initializer (preserving the loop variable, since the increment/condition
  still need it) — and each unwinds inline at its own jump site rather
  than funneling through the loop's shared "condition went false" exit
  path, which avoids a double-pop that a naive single shared cleanup label
  would otherwise cause.

`compiler/src/Disassembler.fs` (new) is the primary way `CodegenTests.fs`
verifies output, per this plan's own testing-strategy note in §7 — no C++
VM needed. `compiler/src/Program.fs` is now a real `iqaloxc` CLI (source
path + output path, running scan → parse → resolve → codegen → write,
stopping and reporting at the first stage with errors) instead of Phase
1's hardcoded demo chunk. `scripts/phase1-roundtrip-smoke-test.sh` (which
depended on `vm/` understanding the same format the frontend now emits) is
retired; `scripts/phase5-compile-smoke-test.sh` replaces it, compiling
every `langspec/examples/*.iqx` fixture with the real CLI with no VM
execution step, since `vm/` can't read v1 until Phase 6.

116 F# tests total (up from 95), including a full v1 serialization suite
(`BytecodeTests.fs`, rewritten for the new format) and `CodegenTests.fs`
covering locals/globals/upvalues, all three bug fixes above, logical
short-circuiting, prefix increment, a `for` loop's condition-exit and
`break` paths converging on the same stack depth without double-popping,
closures, and the `super.method()` self/super-independent-resolution fix.

**Phase 6 — VM core (C++23).** Value representation (a tagged
union/`std::variant` to start; NaN-boxing is a later optimization, not a
Phase 6 requirement), stack-based execution loop, chunk/constant-pool
loading matching `compiler/`'s format, and a mark-sweep tracing garbage
collector (decision 7).

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
expected output, run through both `poc/` and the new `compiler/`+`vm/`
pipeline. Behavioral drift is either an intentional
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
  `langspec/examples/*.iqx` through both `poc/` and `compiler/`+`vm/`
  and diffs output (see Phase 9). This is the project's actual regression
  safety net across two otherwise-independent codebases — worth having
  from the moment there's anything to compare, not bolted on at the end.
- Each side is also independently testable without the other existing yet
  (§3) — `compiler/` against its own disassembled bytecode output, `vm/`
  against hand-assembled bytecode fixtures.

## 8. Tooling & CI

Checked in this environment as of this planning round:

- **C++23**: CMake 3.28.3, GCC 13.3.0, and Clang 18.1.3 are all present and
  `g++ -std=c++23` is accepted — `vm/` prototyping can start immediately.
- **F#/.NET**: no `dotnet` SDK is currently installed here — needs
  provisioning (in this environment or wherever `compiler/` work actually
  happens) before Phase 1's F# half can start for real.
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
  for a full build. Worth being clear-eyed about now that the `compiler/`/
  `vm/` language split is locked in, not discovered later.
- **Deferring field pre-declaration/immutability to `0.2` (decision 6)
  still leaves a live seam**: Phase 8's class design needs to keep fields
  and `var` bindings on deliberately different rules (fields stay
  `0.1-poc`'s always-mutable, no-pre-declaration model; only `var` gets
  `undef`/compile-time-immutability) without that asymmetry silently
  leaking into how classes get implemented — worth a deliberate check at
  the start of Phase 8, not an assumption.

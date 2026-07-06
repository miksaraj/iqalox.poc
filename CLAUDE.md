# CLAUDE.md

Guidance for Claude Code (and other agents) working in this repository.

## What this repo is

Iqalox is a programming language: Lox (from Bob Nystrom's *Crafting Interpreters*),
heavily mutated and extended. This repository holds **multiple implementations
across Iqalox's versions**:

- `poc/` — the **0.1-poc proof-of-concept implementation**: a tree-walking
  interpreter written in Python. Frozen/reference once `0.1` reaches parity
  with it (see `docs/PLAN-0.1.md`) — still the actively-maintained current
  implementation until then.
- `compiler/` — the **0.1 compiler frontend**, written in F#. Scanner →
  parser → AST → resolver → bytecode codegen. In progress; see
  `docs/PLAN-0.1.md`.
- `vm/` — the **0.1 bytecode VM backend**, written in modern C++ (C++23).
  Loads and executes the bytecode `compiler/` emits. In progress; see
  `docs/PLAN-0.1.md`.

Implementation-agnostic material stays at the repository root:

- `langspec/` — the language specification: syntax grammar, lexical grammar,
  precedence rules, and per-version `README.md`s. `langspec/archived/` holds
  frozen snapshots of earlier planning iterations (0.1, 0.2, 0.3) — historical
  record, not to be edited. `langspec/examples/*.iqx` are cross-implementation
  conformance fixtures — same input, same expected output, regardless of
  which implementation runs them.
- `ROADMAP.md` — the version roadmap (0.1-poc onward).
- `docs/` — implementation planning docs (`docs/PLAN-0.1-POC.md` for
  `0.1-poc`, `docs/PLAN-0.1.md` for `0.1`, `docs/LANGUAGE.md` for the
  current language reference).

## Language design authority

**All language design decisions belong to the repository owner.** This includes
syntax, keywords, operator choice and precedence, semantics of any construct,
standard library API shape, and breaking changes between versions.

- Do not invent, guess, or "helpfully" resolve a language design question on
  your own — even a small one (e.g. whether `++x` mutates its operand, whether
  the self-reference keyword is `this` or `self`, whether `concat` is a
  statement or a stdlib function). If an implementation task depends on a
  design question that hasn't been settled, stop and ask rather than picking
  an answer.
- `docs/PLAN-0.1-POC.md` maintains a running list of open design questions
  blocking 0.1-poc work. Check it before implementing anything in that scope,
  and add to it (rather than silently resolving) when you find a new one.
- Engineering decisions — file layout, whether/how to test, tooling, internal
  refactors that don't change observable language behavior — are fair game to
  make and don't need sign-off.

## Implementation language

Only the **0.1-poc** (and any other explicitly-labeled `-poc` milestone) is
implemented in Python. The real, non-PoC implementation starting at `0.1` is
a **compiler frontend in F#** plus a **bytecode VM backend in modern C++
(C++23)** — see `docs/PLAN-0.1.md` for the full plan. Don't assume this
extends to hypothetical future versions beyond `0.1` unprompted; that
hasn't been decided.

## Architecture (`poc/`, 0.1-poc)

Straight out of *Crafting Interpreters*' tree-walk interpreter design:

1. **Scanner** (`poc/src/scanner.py`, `poc/src/token.py`) — source text →
   `Token` list. Handles implicit semicolons (newline → `;`), line comments
   (`#`), block comments (`<# ... #>`), string/number literals, keywords.
2. **Parser** (`poc/src/parser.py`) — recursive-descent, produces an AST of
   `Expr`/`Stmt` nodes (`poc/src/expression.py`, `poc/src/statement.py`).
3. **Interpreter** (`poc/src/interpreter.py`) — visitor-pattern tree-walking
   evaluator, over `poc/src/environment.py` for variable scoping and mutability.
4. **Errors** (`poc/src/error.py`) — `ParseError`, `IqaloxRuntimeError`.
5. **Entry point** (`poc/src/iqalox.py`) — REPL and file-runner (`Iqalox`
   class), mirrors the `jlox`/error-reporting structure from the book.
6. **`poc/src/ast_printer.py`** — debug helper for visualizing the AST.

`poc/src/expression.py` and `poc/src/statement.py` are **generated files**.
To add or change an AST node, edit the `EXPRESSIONS` / `STATEMENTS` dict in
`poc/tools/generate_ast.py` and regenerate:

```
python poc/tools/generate_ast.py poc/src
```

Don't hand-edit the structure of the generated classes directly — the next
regeneration will silently discard the change. (Bug fixes to
`generate_ast.py`'s templating are fine, just regenerate afterward.)

`langspec/SYNTAX_GRAMMAR.md` is kept in sync with `poc/src/parser.py` as of
this writing (see its own header notes for anything still flagged stale).
Keep it and the language README in sync as grammar actually lands in
whichever implementation currently leads — but don't treat a stale grammar
doc as authorization to implement something differently than what's
actually specified elsewhere.

## Architecture (`compiler/` + `vm/`, 0.1)

See `docs/PLAN-0.1.md` for the full plan — the compiler frontend (`compiler/`,
F#) and bytecode VM backend (`vm/`, C++23), decoupled through a versioned
bytecode file. `vm/src/bytecode.hpp` still implements format v0 (a minimal
container just big enough to represent "push a string constant, print it,
halt") — `compiler/` has moved on to format v1 as of Phase 5 (see below);
rebuilding `vm/` to read v1 is Phase 6's job, one phase behind by design.
Phase 1 (toolchain scaffolding and
an end-to-end round-trip proof) is done. Phase 2 (the scanner) is also
done: `compiler/src/Token.fs` (an idiomatic `TokenType` discriminated
union) and `compiler/src/Scanner.fs` (`Scanner.scanTokens`) — see
`docs/PLAN-0.1.md`'s Phase 2 entry for the several `poc` scanner bugs this
surfaced and fixed rather than carried forward (decimal literals,
leading-underscore identifiers, and others). Phase 3 (the parser) is also
done: `compiler/src/Ast.fs` (an idiomatic `Expr`/`Stmt` discriminated
union) and `compiler/src/Parser.fs` (`Parser.parse`) — see
`docs/PLAN-0.1.md`'s Phase 3 entry for a real naming collision between
`TokenType` and `Ast` case names (fixed by suffixing the colliding `Ast`
cases) and two more `poc` bugs found and fixed rather than carried
forward. Phase 4 (the resolver) is also done: `compiler/src/Bound.fs`
(`BoundExpr`/`BoundStmt`, the same shape as `Ast.fs` but with every
variable reference/declaration/`self`/`super` carrying its resolved
binding) and `compiler/src/Resolver.fs` (`Resolver.resolve`), implementing
`clox`'s compile-time scope/slot/upvalue algorithm — see
`docs/PLAN-0.1.md`'s Phase 4 entry for how compile-time immutability
enforcement, self-referencing classes, and `self`/`super` scoping all work.
Phase 5 (code generation) is also done: `compiler/src/Bytecode.fs` now
defines bytecode format v1 (a structured, index-based `Instruction`/
`Chunk`/`Constant`/`FunctionProto` representation, serialized to actual
bytes only in `Bytecode.write`), `compiler/src/Disassembler.fs`
pretty-prints a `Chunk` (the primary way tests verify codegen output, no
C++ VM needed), and `compiler/src/Codegen.fs` (`Codegen.compile`) lowers
`Bound.fs`'s tree to it — see `docs/PLAN-0.1.md`'s Phase 5 entry for the
stack-depth-tracking model, the three more `poc` bugs (comma, `??`,
elvis) fixed here, a real gap found in already-merged Phase 4 code
(`Bound.BSuper` needed `self`'s binding alongside `super`'s to support a
super-call from inside a closure nested within a method) and fixed in
both `Bound.fs`/`Resolver.fs`, and the class/method and `for`-loop
`break`/`continue` codegen sequences. `compiler/src/Program.fs` is now a
real `iqaloxc` CLI (source path + output path, scan → parse → resolve →
codegen → write).

Phase 6 (VM core) is also done: `vm/src/value.hpp` defines `Value` as a
tagged `std::variant` (`nil`/`undef`/`bool`/`double`/`Obj*`);
`vm/src/object.hpp` defines the heap-object hierarchy (`ObjString`,
`ObjVector`, `ObjFunction`, `ObjClosure`, `ObjUpvalue`); `vm/src/bytecode.cpp`
loads format v1 (superseding Phase 1's v0 loader); `vm/src/vm.hpp`/`.cpp`
is the stack-based interpreter (`Vm::run`) plus its mark-sweep tracing
garbage collector (decision 7), both on one `Vm` class. Covers every
`0.1-poc`-equivalent expression/statement plus functions and closures —
see `docs/PLAN-0.1.md`'s Phase 6 entry for three notable design points
(a calling convention that deliberately differs from `clox`'s, since
`Resolver.fs` doesn't reserve frame slot 0 for the callee; no dedicated
"close upvalue" opcode, handled instead by a universal stack-shrink choke
point in `Vm`; and why `ObjUpvalue` addresses its stack slot by index
rather than by pointer, to sidestep undefined behavior comparing pointers
into different blocks of the stack's underlying `std::deque`) and a real
bug caught before it shipped (the GC mustn't run at all until the loaded
program's top-level closure is anchored on the stack, or it frees the
whole program before it starts). Classes/`self`/`super` were recognized by
the loader and the VM's opcode dispatch at this point but raised a clear
"not yet supported" error if actually executed until Phase 8 (below).

Phase 7 (native standard library) is also done: `vm/src/object.hpp` adds
`ObjNativeFunction` (a name, arity, and a plain C++ function pointer, no
bytecode frame involved in calling one), and `vm/src/natives.hpp`/`.cpp`
implement `print`/`concat`, both defined as globals by the `Vm`
constructor — mirroring `poc/src/interpreter.py`'s `Interpreter.__init__`,
which defines both the same way before any user statement runs. See
`docs/PLAN-0.1.md`'s Phase 7 entry for `stringify`'s float-formatting
work (matching Python's exact fixed/scientific notation thresholds, which
`std::to_chars`' own formatting doesn't), the vector-nested-element
`repr`-vs-`stringify` distinction, a real `poc` bug found (`concat` on a
non-vector argument crashes with an uncaught Python exception instead of
a clean error) and not carried forward, and a `Resolver.fs` fix so
reassigning/redeclaring `print`/`concat` is a compile-time error like any
other global instead of silently succeeding. `scripts/phase7-run-smoke-test.sh`
(replacing Phase 5's compile-only script, now that `vm/` can actually
execute a program and produce real output) compiles *and runs* every
`langspec/examples/*.iqx` fixture — a hand-verified spot check during this
phase found the four non-class examples already produce byte-for-byte
identical output to `poc`. Classes/`self`/`super` still raised a "not
yet supported" error at that point in time.

Phase 8 (classes & OOP) is also done: `vm/src/object.hpp` adds
`ObjClass` (a name plus a `methods` map, populated by *copying* the
superclass's methods at `Inherit` time — clox's static-copy approach,
not `poc`'s live `superclass` pointer chain walked at every lookup;
behaviorally equivalent since Iqalox has no runtime method-adding
syntax), `ObjInstance` (a `klass` pointer plus a `fields` map that
springs entries into existence on first assignment, exactly like
`poc/src/callable.py`'s `IqaloxInstance` — 0.1's new compile-time
immutability is scoped to `var` bindings only, never fields), and
`ObjBoundMethod` (mirrors `poc`'s `IqaloxFunction.bind`). Method calls
need a different calling convention from plain function calls, since
`Resolver.fs` reserves frame slot 0 for `self` on methods but not on
plain functions: `vm/src/vm.cpp` adds a `callMethod` path alongside the
existing `call`, and `CallFrame` gains `resultIndex` (generalizing what
used to be hardcoded as `stackBase - 1` in `Return`'s handler, since it
differs between the two conventions) and `isInitializer` (lets `Return`
substitute the pre-seeded instance for whatever `init` explicitly
returns, matching `poc`'s `IqaloxClass.call` discarding `init`'s own
return value — a direct `.init()` call on an existing instance, not via
construction, does *not* get this treatment, also matching `poc`).
Dynamic dispatch (`GetProperty`) always resolves against the receiver's
actual runtime `klass`, so an inherited method's `self.method()` call
correctly finds a subclass's override; `super.method()` (`GetSuper`)
bypasses dispatch entirely, resolving directly against the specific
superclass value captured at class-declaration time. See
`docs/PLAN-0.1.md`'s Phase 8 entry for the full opcode-by-opcode stack
shapes, exact error-message porting from `poc`, and a real bug found and
fixed in already-merged Phase 5 code: `Codegen.fs`'s `CompileClass`
never popped the synthetic `super` local's stack slot once a class's
own methods were compiled, so a *second*, unrelated `extends`
declaration later in the same script reused the same slot per
`Resolver.fs`'s (correct) scoping but collided with the first class's
still-resident value at runtime — surfaced only by real end-to-end
script validation (`inheritance.iqx` vs `poc`'s output), not by any unit
test, and fixed with one added `Pop`. All six `langspec/examples/*.iqx`
fixtures, including the two class-based ones, now produce byte-for-byte
identical output to `poc`.

## Engineering conventions

- **`poc/` Python style**: match what's there — 4-space indents, type hints
  on public methods, `ABC`/visitor pattern for AST nodes, no
  docstrings/comments beyond the occasional `# TODO [#n]: ...` marker.
- **`poc/` testing**: standardizes on **pytest**. Run with `cd poc && pytest`
  (`poc/pytest.ini` sets `pythonpath = src`; dev dependencies are in
  `poc/requirements-dev.txt`). When adding non-trivial interpreter behavior,
  add tests under `poc/tests/` rather than relying on manual `.iqx` script
  runs alone. Note: `poc/src/token.py` shadows the stdlib `token` module —
  `poc/tests/conftest.py` works around this by evicting it from
  `sys.modules` before importing project modules; see
  `docs/PLAN-0.1-POC.md` §5 if that ever needs revisiting.
- **`compiler/`/`vm/` testing**: xUnit (F#) and Catch2 (C++) respectively,
  per `docs/PLAN-0.1.md` §7 — plus `scripts/conformance-test.sh` (Phase 9),
  a CI job running every `langspec/examples/*.iqx` fixture through both
  `poc/` and `compiler/`+`vm/` and diffing output byte-for-byte.
- **`compiler/` F# style**: targets the current F# language version (F# 10
  as of .NET 10) — prefer newer idioms where they cleanly simplify existing
  code (e.g. a discriminated union's auto-generated `.IsCaseName` property,
  F# 9, over a two-arm `match` that only needed a case check, not its
  payload) rather than defaulting to older patterns out of habit. Don't
  force-fit a newer feature where it doesn't clearly help, though — e.g.
  nullable reference types (F# 9) have no real application here, since this
  codebase doesn't interoperate with null-returning APIs.
- **Example scripts**: live in `langspec/examples/*.iqx` (current) —
  `langspec/archived/*/examples/*.iqlx` used the old extension and are
  frozen. These are cross-implementation conformance fixtures: keep them
  runnable as features land in *any* implementation; if an example depends
  on an unresolved design question, leave a note rather than changing the
  example to match a guess.
- **Commit style**: short, lowercase, imperative summaries (see `git log`) —
  no enforced conventional-commits format.

## GitHub Actions

**Any GitHub Actions workflow added to this repo must pin every `uses:` step
to a full commit SHA, never a floating tag or branch** (not `actions/checkout@v4`,
but `actions/checkout@<full-40-char-sha>`), including for first-party GitHub
actions. This is a supply-chain security requirement, not a style preference —
don't relax it for convenience.

## License

GPLv3 (`LICENSE`). No file header convention is currently in use — don't add
license headers to source files unless asked.

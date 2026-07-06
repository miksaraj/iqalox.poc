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
bytecode file (currently format v0 — a minimal container just big enough
to represent "push a string constant, print it, halt"; see
`vm/src/bytecode.hpp` for the authoritative layout, mirrored by
`compiler/src/Bytecode.fs`'s writer). Phase 1 (toolchain scaffolding and
an end-to-end round-trip proof) is done; the real scanner/parser/resolver/
codegen (`compiler/`) and VM core/GC/stdlib (`vm/`) are not built yet —
this section will grow into the same level of detail as the `poc/` one
above as that work lands. `scripts/phase1-roundtrip-smoke-test.sh` builds
both and proves `compiler/`'s output loads and runs correctly in `vm/`.

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
  per `docs/PLAN-0.1.md` §7 — plus a cross-implementation conformance job
  running `langspec/examples/*.iqx` through both `poc/` and
  `compiler/`+`vm/` and diffing output.
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

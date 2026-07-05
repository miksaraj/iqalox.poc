# CLAUDE.md

Guidance for Claude Code (and other agents) working in this repository.

## What this repo is

Iqalox is a programming language: Lox (from Bob Nystrom's *Crafting Interpreters*),
heavily mutated and extended. This repository (`iqalox.poc`) holds the **first
proof-of-concept implementation**: a tree-walking interpreter written in Python.

- `langspec/` — the language specification: syntax grammar, lexical grammar,
  precedence rules, and per-version `README.md`s. `langspec/archived/` holds
  frozen snapshots of earlier planning iterations (0.1, 0.2, 0.3) — historical
  record, not to be edited.
- `src/` — the Python tree-walk interpreter (scanner → parser → AST → interpreter).
- `tools/generate_ast.py` — generates `src/expression.py` and `src/statement.py`
  from the `EXPRESSIONS` / `STATEMENTS` dicts at the top of the script.
- `ROADMAP.md` — the version roadmap (0.1-poc onward).
- `docs/` — implementation planning docs (e.g. the detailed plan to reach 0.1-poc).

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
implemented in Python. The implementation language for the real, non-PoC
releases has not been decided — don't assume Python carries forward, and don't
make that decision unprompted.

## Architecture (current PoC)

Straight out of *Crafting Interpreters*' tree-walk interpreter design:

1. **Scanner** (`src/scanner.py`, `src/token.py`) — source text → `Token` list.
   Handles implicit semicolons (newline → `;`), line comments (`#`), block
   comments (`<# ... #>`), string/number literals, keywords.
2. **Parser** (`src/parser.py`) — recursive-descent, produces an AST of
   `Expr`/`Stmt` nodes (`src/expression.py`, `src/statement.py`).
3. **Interpreter** (`src/interpreter.py`) — visitor-pattern tree-walking
   evaluator, over `src/environment.py` for variable scoping and mutability.
4. **Errors** (`src/error.py`) — `ParseError`, `IqaloxRuntimeError`.
5. **Entry point** (`src/iqalox.py`) — REPL and file-runner (`Iqalox` class),
   mirrors the `jlox`/error-reporting structure from the book.
6. **`src/ast_printer.py`** — debug helper for visualizing the AST.

`src/expression.py` and `src/statement.py` are **generated files**. To add or
change an AST node, edit the `EXPRESSIONS` / `STATEMENTS` dict in
`tools/generate_ast.py` and regenerate:

```
python tools/generate_ast.py src
```

Don't hand-edit the structure of the generated classes directly — the next
regeneration will silently discard the change. (Bug fixes to
`generate_ast.py`'s templating are fine, just regenerate afterward.)

`langspec/SYNTAX_GRAMMAR.md` is a **work-in-progress** grammar and currently
lags behind the implementation (e.g. it's missing arrays, `mut`, the elvis
`?:` form). Keep it and the language README in sync as grammar actually lands
in the parser — but don't treat a stale grammar doc as authorization to
implement something differently than what's actually specified elsewhere.

## Engineering conventions

- **Python style**: match what's there — 4-space indents, type hints on
  public methods, `ABC`/visitor pattern for AST nodes, no docstrings/comments
  beyond the occasional `# TODO [#n]: ...` marker.
- **Testing**: the project standardizes on **pytest**. Run with `pytest` from
  the repo root (`pytest.ini` sets `pythonpath = src`; dev dependencies are in
  `requirements-dev.txt`). When adding non-trivial interpreter behavior, add
  tests under `tests/` rather than relying on manual `.iqx` script runs alone.
  Note: `src/token.py` shadows the stdlib `token` module — `tests/conftest.py`
  works around this by evicting it from `sys.modules` before importing
  project modules; see `docs/PLAN-0.1-POC.md` §5 if that ever needs revisiting.
- **Example scripts**: live in `langspec/examples/*.iqx` (current) —
  `langspec/archived/*/examples/*.iqlx` used the old extension and are frozen.
  Keep the current examples runnable as features land; if an example depends
  on an unresolved design question, leave a note rather than changing the
  example to match a guess.
- **Commit style**: short, lowercase, imperative summaries (see `git log`) —
  no enforced conventional-commits format.

## GitHub Actions

**Any GitHub Actions workflow added to this repo must pin every `uses:` step
to a full commit SHA, never a floating tag or branch** (not `actions/checkout@v4`,
but `actions/checkout@<full-40-char-sha>`), including for first-party GitHub
actions. This is a supply-chain security requirement, not a style preference —
don't relax it for convenience. No workflows exist yet as of this writing; this
rule applies from the first one added onward.

## License

GPLv3 (`LICENSE`). No file header convention is currently in use — don't add
license headers to source files unless asked.

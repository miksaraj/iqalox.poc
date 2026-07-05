# Plan: getting to 0.1-poc

Status snapshot based on reading `src/` against the `0.1-poc` feature list in
`ROADMAP.md` and the example scripts in `langspec/examples/*.iqx`. This is a
living document — update the status table and the open-questions list as work
lands or decisions get made. See `CLAUDE.md` for why the open questions are
listed rather than answered here.

## 1. Design decisions (resolved)

These aren't engineering choices; each one changes how the language behaves,
so they were routed to the repo owner per `CLAUDE.md` rather than guessed at.
Resolved 2026-07-05:

1. **Ternary fully replaces `if`/`else`, including for statement-like
   branches.** `continue`, `break`, and calls with side effects (`print k`)
   must all be usable as ternary branches, e.g.:
   ```
   (k == 0 or k == 5) ? continue : (k == 10) ? break : print k
   ```
   Engineering consequence: the ternary's middle/right branches can't be
   restricted to the `Expr` hierarchy the way `Ternary` is defined today —
   they need to accept statement-like constructs too (at minimum `break`/
   `continue` control-flow signals, plus ordinary expression-statements now
   that `print`/`concat` are calls rather than dedicated statements — see
   decision 4). The cleanest fit is probably: parse ternary branches via a
   grammar rule that covers both plain expressions and `break`/`continue`;
   evaluate `break`/`continue` branches by raising the same control-flow
   signal a loop body would use (see §4 step 4), letting it propagate up
   through ternary evaluation to the enclosing `for`. A ternary with a
   `break`/`continue` branch has no meaningful "value" on that arm — that's
   fine, since taking that arm never falls through to produce one.

2. **Prefix `++`/`--` uses standard mutating semantics.** `++x` assigns
   `x + 1` back to `x` and evaluates to the new value (matching every example
   that uses it: `++i` in `for` headers, `++c |> print` in `createCounter`).
   Reassigning an immutable (no `mut`) target should **preferably** be a
   compile-time error. Note: there is currently no static analysis/resolution
   pass in this codebase (no Lox-style `Resolver`) — one doesn't exist until
   the "disallow unused variables" work in 0.3/0.5 introduces it. Recommended
   approach for 0.1-poc: implement immutable-target rejection as a **runtime**
   error for now (reusing the existing `Environment.assign` mechanism, which
   already does exactly this for plain `=` assignment), and fold true
   compile-time enforcement into whatever static-analysis pass 0.3 introduces
   rather than building a one-off resolver just for this. Flagging this as a
   deliberate interim tradeoff, not a silent downgrade of the requirement.
   Also: `increment()` in the parser must reject non-assignable operands
   (only `Variable`, and later property-access, targets — not arbitrary
   expressions like `++5`).

3. **The self-reference keyword is `self`, not `this`.** `src/token.py`'s
   `TokenType.SELF = 'self'` is canonical; every `this` reference in current
   (non-archived) langspec docs/examples was outdated and has been corrected
   (see `langspec/examples/classes.iqx`, `langspec/SYNTAX_GRAMMAR.md`).
   Archived snapshots under `langspec/archived/` are left as-is (historical
   record).

4. **No function call — builtin or user-defined — takes parentheses.**
   `print`/`concat` are promoted to ordinary (builtin) function values, not
   statement keywords, and called the same way every other function is:
   without parens (`print x`, `concat [a, b]`, `add5 1`, `math.square 3`).
   This generalizes what was originally asked only for `print`/`concat` to
   *all* calls, resolving the "4b" exploration from an earlier draft of this
   doc, using the fixed-arity-juxtaposition option that was flagged there as
   the recommendation. Finalized shape, worked out from the earlier
   tradeoffs:
   - **Fixed-arity juxtaposition, no currying.** `fun f(a, b) {...}` keeps
     its existing declaration form; a call site provides exactly that many
     arguments, space- (and, for 2+, comma-) separated, immediately after
     the callee — see the grammar sketch below.
   - **Compound (non-primary) arguments need grouping parens.**
     `fact(n - 1)` becomes `fact (n - 1)` — parens here mean "group this
     sub-expression into one argument," not "this is the argument list."
     Simple single-token arguments need nothing (`add5 1`, `Duck "Waddles"`).
   - **Zero-argument calls keep explicit `()`** — `count()`, `B()`,
     `duck.quack()` — so a bare name can still mean "the function value
     itself, don't call it" (`print fact`, `return adder`, `test count`).
     Without this, first-class function values (already used throughout
     `functions.iqx`) would have no way to be passed around unevaluated.
   - **2+ arguments are comma-separated without an enclosing pair**:
     `ifEqualOr 2, 5`. The comma here is an argument separator, not the
     general comma operator — scoped out exactly like
     `Parser.comma_as_operator` already gets toggled off while parsing a
     `[...]` vector literal, just triggered by "currently parsing a bare
     call's argument list" instead.
   - **Nested calls need no extra parens**, because a call is itself a
     `primary`-level unit once fully parsed: `print concat [self.name, "quacks"]`
     parses as `concat` immediately followed by the vector-literal primary
     `[self.name, "quacks"]` — forming the complete call `concat([...])` —
     which then becomes `print`'s one argument, i.e.
     `print(concat([self.name, "quacks"]))`. No comma appears between
     `print` and `concat`, so `print` never tries to treat `concat` and the
     vector as two separate arguments.

   Rough grammar sketch (to be refined when functions/calls are actually
   implemented — see §4 step 5):
   ```
   call       → IDENTIFIER arguments? ( "." IDENTIFIER arguments? )*
   arguments  → "(" ")"                     ; explicit zero-arg call
              | argument ( "," argument )*  ; 1+ args, no wrapping parens
   argument   → "(" expression ")"          ; grouped/compound argument
              | primary
              | call                        ; nested call, e.g. `concat [...]`
   ```
   `src/statement.py`'s `Print`/`Concat` statement nodes need to go away
   once function calls exist and `print`/`concat` are registered as builtin
   function values in the global environment instead (§4 step 6).

5. **Mixins and traits are deferred out of 0.1-poc**, moved to `0.2` in
   `ROADMAP.md`. The PHP-vs-Scala-style implementation question from the
   original notes is unresolved and doesn't need to be until that work
   starts; `module`/`trait`/`use`/`with`/`extends` tokens already exist but
   have no grammar or semantics yet, and none should be built for 0.1-poc.

6. **"Support for standard methods" means baseline Lox method dispatch** —
   plain method definitions on classes, called via `instance.method(...)`.
   No built-in methods on primitive values (e.g. `.length()` on a string) are
   in scope for 0.1-poc; that's stdlib territory for a later version.

Additionally confirmed by re-reading history: `while` was **deliberately
removed** from the language (commit `1a22361`, "Removed while loop from
examples... since while was removed from the language specification").
`for` is the only loop construct. `langspec/SYNTAX_GRAMMAR.md` still listed
`whileStmt` and `ifStmt` (superseded by decision 1) — both removed from the
grammar draft as part of this update.

## 2. Status of 0.1-poc feature list

Legend: ✅ done · 🟡 partial/buggy · ⛔ not started

| Feature | Status | Notes |
|---|---|---|
| Array support (vectors) | 🟡 | `Vector` expression + `visit_vector_expr` work for literals (`[1, 2, 3]`). No indexing, no mutation, no stdlib methods. Matrices/manipulation stdlib are 0.2 scope. |
| Block comments `<# ... #>` | ✅ | Fixed — see §3. |
| Implicit semicolons | ✅ | Newline → `SEMICOLON` token in `Scanner.scan_token`. A stray/empty semicolon (blank line, comment-only line) is now a harmless no-op statement instead of a hard parse error — see §3. |
| `continue` / `break` statements | ✅ | Implemented as zero-field `Expr` nodes (`Break`/`Continue`), not statements — see decision 1's engineering note. Usable anywhere an expression is (bare, or as a ternary branch); raise `BreakSignal`/`ContinueSignal` in the interpreter, caught by the enclosing `for`. |
| Prefix `++`/`--` | ✅ | Mutates via `Environment.assign` (so the existing immutability check applies), evaluates to the new value. Parser rejects non-`Variable` targets (`++5` is a parse error). |
| `extends` for inheritance | ⛔ | Still not started — no `class` declaration parsing/interpretation at all yet. |
| Chainable ternary (elvis `?:` too) instead of if/else | ✅ | `Ternary` node, both `? :` and `?:` forms parse and evaluate; nesting gives chaining. `break`/`continue` work as branches because they're expressions now (see above), not because `Ternary` itself changed. |
| Support for standard methods | ⛔ | Baseline Lox method dispatch on classes (decision 6) — depends on classes existing at all. |
| No `+` string concat | 🟡 | Unchanged from before — `+` on two strings raises a runtime error today only as a side effect of numeric type-checking, not an intentional string-specific rule. |
| `print` / `concat` as builtin functions | ⛔ | Still dedicated `Stmt` subclasses — depends on functions/calls existing (§4 step 5) and its own migration (§4 step 6). |
| Pipe operator `\|>` | ⛔ | Token exists; not parsed at all. Depends on `print`/`concat` being callable values and on functions existing. |
| Ignore operator `_` | ⛔ | Token exists (`UNDERSCORE`); no parsing/semantics. |
| Nullable infix `??` | ✅ | Parsed in `multiplication()` (questionable precedence placement, see §3) and evaluated in `visit_binary_expr`. |
| Comma operator | ✅ | `comma()` in parser, precedence matches the table in the root `README.md`. |
| Immutability by default (`mut`) | ✅ | `VariableData.is_mutable`, enforced in `Environment.assign`. The `var IDENTIFIER mut? = expr` parse path had a double-`advance()` bug that made `mut` declarations unparseable in practice — fixed, see §3. |
| `for` loops | ✅ | Full grammar (initializer/condition/increment all optional, per the drafted grammar minus the removed `whileStmt`). Loop-scoped `Environment` wraps the initializer; body executes via the normal `Block` mechanics. |
| Logical `and`/`or` | ✅ | `Logical` expr node, short-circuit evaluation, sits between `ternary` and `equality` in precedence (`ternary → logic_or → logic_and → equality`). |

Mixin support and trait support are **out of scope for 0.1-poc** (deferred to
0.2, decision 5) and are intentionally not in this table.

Not on the 0.1-poc list but required by the example scripts and by Lox
baseline, still **not implemented**:

| Missing baseline | Notes |
|---|---|
| Functions (`fun`, calls, `return`, closures) | No `Function`/`Call`/`Return` AST nodes, no parsing. Needed for essentially all of `functions.iqx` and for the pipe operator to be useful. |
| Classes (`class`, methods, instances, `init`, `super`, `self`) | No AST nodes, no parsing. Needed for `classes.iqx`/`inheritance.iqx`. |

## 3. Known bugs

### Fixed as part of implementing steps 1-4 (2026-07-05 batch)

Writing the first real tests against this interpreter (see §5) surfaced
several bugs well beyond the one originally documented here — the honest
takeaway is that essentially no multi-statement `.iqx` source had ever been
successfully run end-to-end before this batch, since several of these compound
to make basically any real script fail. All are plain correctness bugs, not
design questions, so fixed directly rather than routed for sign-off:

1. **Block comment terminator** (the one originally documented here).
   `Scanner.scan_token`'s block-comment loop exited as soon as *either*
   `peek() == '#'` *or* `peek_next() == '>'` individually, not when the
   two-character sequence `#>` was actually next. Fixed to advance while
   **not** (`peek() == '#' and peek_next() == '>'`); also now tracks
   newlines inside block comments so line numbers stay accurate afterward.
2. **`TokenType` didn't mix in `str`,** so every `some_char == TokenType.X`
   comparison against a plain scanned character was silently always `False`
   (an `Enum` member never equals a raw string unless it also subclasses
   `str`). This broke, in order of how badly: implicit-semicolon insertion
   (`c == TokenType.NEWLINE` never matched — newlines were reported as
   "unexpected character" instead of becoming semicolons), line comments
   (`token == TokenType.COMMENT` never matched, so `# ...` text leaked into
   the token stream as if it were code), and block comments (same issue for
   `TokenType.BLOCK_COMMENT_START`, meaning fix #1 above was previously dead
   code — the branch containing it was never entered). Fixed by declaring
   `class TokenType(str, Enum)`; verified no two members share a value
   (which would otherwise alias under the `str` mixin).
3. **Scanner's number/identifier dispatch tested the wrong character.**
   `scan_token()` decides whether to start scanning a number or identifier
   via `self.is_digit()`/`self.is_alpha()`, but those check `self.peek()` —
   the *next* character — not `c`, the one `scan_token` had just consumed.
   This is correct inside `number()`/`identifier()`'s own "keep consuming"
   loops (peeking ahead before advancing), but wrong at the initial dispatch
   site, where it needs to test `c` itself. Net effect: any single-character
   identifier or number adjacent to a delimiter (`i`, `n`, `x`, `0`...`9`
   alone) was misscanned as "unexpected character" — i.e. most loop counters
   and small integer literals in real programs. Fixed by giving
   `is_digit`/`is_alpha`/`is_alpha_numeric` an optional explicit-character
   parameter (defaulting to `peek()` for existing call sites) and passing
   `c` at the dispatch site.
4. **`true`/`false`/`nil` literals stored the `TokenType` enum member as
   their value**, not the Python value it stands for (`Literal(TokenType.FALSE)`
   instead of `Literal(False)`, etc.). Combined with bug #2's fix, this
   would have made `false` and `nil` evaluate as *truthy* (`is_truthy`
   checks `isinstance(obj, bool)`/`obj is None`, neither of which match a
   `TokenType` member). Fixed in `Parser.primary()` to use real `False`/
   `True`/`None`.
5. **`var` declarations' `mut` handling double-advanced.**
   `if self.match(TokenType.MUTABLE): is_mutable = True; self.advance()` —
   `match()` already consumes the `mut` token on success, so the extra
   `self.advance()` silently ate the *next* token too (typically `=`),
   making every `mut` declaration fail to parse. Fixed by dropping the
   redundant `advance()`.
6. **A blank or comment-only line aborted the entire program.** Every
   newline becomes a semicolon (by design), so a blank line produces a
   semicolon with no preceding expression. `expression_statement()`
   unconditionally tried to parse an expression there and failed, which
   (a) got "recovered" via `synchronize()` but still set `Iqalox.had_error`,
   and `Iqalox.run()` refuses to interpret anything at all once that flag is
   set — so *any* script with a blank line between statements (i.e. every
   realistic script, including every example in `langspec/examples/`) never
   actually ran; and (b) left a `None` in the parsed statement list from the
   failed/recovered declaration, which then crashed the interpreter with a
   raw `AttributeError` instead of a clean error. Fixed by having
   `Parser.statement()` treat a leading `SEMICOLON` as a no-op (returns
   `None` without raising), and having `Parser.parse()`/`Parser.block()`
   filter `None` out of the collected statement list (covering both this
   case and ordinary parse-error recovery).
7. **`src/iqalox.py` called `main(argv[1:])` unconditionally at module
   level**, with no `if __name__ == '__main__':` guard. Since `scanner.py`
   and `parser.py` both do a lazy `import iqalox` (to call back into
   `Iqalox.error(...)` without a circular top-level import), simply
   *importing* the scanner or parser module for any reason — including from
   a test suite — silently started a whole REPL session as a side effect.
   Fixed with the standard guard.
8. **Runtime errors were caught and silently discarded.**
   `Interpreter.interpret()` had `except IqaloxRuntimeError: return` with no
   call to `Iqalox.runtime_error(...)` — the method existed and was clearly
   intended for this, just never wired up. This mattered concretely for
   decision 2: an unenforced-looking immutability error is not the same
   thing as an enforced one. Fixed to call `Iqalox.runtime_error(error)`
   (and added minimal, non-crashing reporting for `break`/`continue` used
   outside of a loop, which is a plain `Exception` internally and has no
   token to build a "proper" error from).

### Still open (not addressed in this batch)

1. **`??` precedence.** `DOUBLE_QUESTION_MARK` is matched inside
   `multiplication()`, i.e. it currently binds as tightly as `*`/`/`/`%`/`^`.
   The root `README.md` precedence table doesn't mention `??` at all. Worth
   deciding (design call, add to §1 if you want it there) where it should
   actually sit — most languages put null-coalescing near the bottom, close
   to ternary/assignment.
2. `error.py`'s `IqaloxRuntimeError.__str__`/`__repr__` just call `super()`,
   i.e. they're no-ops — fine to leave, but not worth keeping if nobody
   relies on the override.

## 4. Suggested sequencing

With §1 resolved, all of the following can proceed without further design
sign-off — flag anything that turns up a *new* design question rather than
deciding it inline (per `CLAUDE.md`).

**Steps 1-4, plus step 8, are done** (branch `claude/0.1-poc-control-flow`,
2026-07-05). Step 8 (prefix `++`/`--` mutation) was pulled forward from its
original position because `for` loops are untestable — infinite-looping —
without a working increment, and every existing loop example relies on
`++i`/`++j`/`++k`. See §2/§3 for what actually landed and what bugs surfaced
along the way, and §5 for the new test suite. `loops.iqx` and `functions.iqx`
had their loop counters updated to declare `mut` (a direct, non-design
consequence of the already-decided immutability rule, not a new decision) —
they still won't fully run end-to-end because they also depend on the pipe
operator and/or functions/classes (steps 5-7, 10), which remain undone.

1. ~~**Fix known bugs**~~ — done, see §3.
2. ~~**Logical `and`/`or`**~~ — done.
3. ~~**`for` loops**~~ — done.
4. ~~**`break`/`continue`**~~ — done, but *not* via the control-flow-exception-
   from-a-statement-position approach originally sketched here. Simpler
   approach actually used: `Break`/`Continue` are zero-field `Expr` nodes
   (parsed in `primary()`), so they're usable anywhere any expression is —
   bare, as a ternary branch, wherever — with no special-casing of `Ternary`
   needed at all. Evaluating one just raises `BreakSignal`/`ContinueSignal`
   (plain `Exception` subclasses in `interpreter.py`), which propagates
   naturally up through however many nested expression evaluations until
   `visit_for_stmt`'s per-iteration `try/except` catches it.
5. **Functions** (`fun`, `Call`, `Return`, closures over `Environment`) —
   standard Lox mechanics; needed for almost everything else including the
   pipe operator and for `print`/`concat` to become callable. The `Call`
   grammar itself is the paren-free form from decision 4 (fixed-arity
   juxtaposition, `()` only as the explicit zero-arg marker, comma-separated
   2+ args) — there is no separate parenthesized-call form to build first
   and migrate away from; build the paren-free grammar directly.
6. **Promote `print`/`concat` to builtin functions** (decision 4) — register
   them as native function values in the global environment (rather than
   `Print`/`Concat` statement AST nodes), retire the dedicated
   `print`/`concat` statement grammar in `parser.py`, and remove `Print`/
   `Concat` from `tools/generate_ast.py`'s `STATEMENTS` dict once nothing
   depends on them. They use the exact same call grammar as user-defined
   functions (step 5) — no special-casing needed now that no call takes
   parens.
7. **Pipe operator `|>`** — once functions and step 6 land, desugar
   `a |> f` to a call `f(a)`.
8. ~~**Prefix `++`/`--` mutation**~~ — done (pulled forward, see above).
9. **Ignore operator `_`** — per decision 1, needs to work as a ternary
   branch (a no-op arm).
10. **Classes** — `class`, `extends`, `init`, methods, `super`, `self` (not
    `this`, decision 3). Getters/setters and mixins/traits explicitly stay
    out of scope (0.2, decision 5).
11. **Sync `langspec/SYNTAX_GRAMMAR.md` and `langspec/README.md`** with
    whatever actually got built. The grammar draft has already been patched
    for the `while`/`if`/`this` removals (§1), but still needs real
    `printStmt`→builtin-call, ternary-with-statement-branches, function, and
    class grammar once those land.
12. ~~**Backfill pytest coverage**~~ — started, see §5. Keep extending it as
    steps 5+ land rather than after the fact.

Steps 2–3 have no dependency on anything else in this list and could start
immediately.

## 5. Test suite

`tests/` (pytest, per `CLAUDE.md`) now covers the scanner/parser/interpreter
behavior from steps 1-4 and 8: implicit semicolons and empty-statement
tolerance, single-character identifier/number scanning, line and block
comments (including the terminator regression), `true`/`false`/`nil` literal
values, logical `and`/`or` short-circuiting and precedence, `for` loops with
all clauses present/omitted, `break`/`continue` as bare statements and as
ternary branches, and prefix `++`/`--` mutation plus the immutable-target
runtime error. Run with `pytest` from the repo root (`pytest.ini` sets
`pythonpath = src`).

One infrastructure wrinkle worth knowing about: `src/token.py` shadows the
Python standard library's `token` module. Pytest's own bootstrap imports the
real stdlib `token` before test collection runs, caching it in
`sys.modules`, so `tests/conftest.py` has to explicitly evict that cache
entry (`sys.modules.pop('token', None)`) after inserting `src` onto
`sys.path`, or every project import resolves to the wrong `token` module.
This is a test-harness-only workaround — nothing in `src/` changed for it —
but if this file ever gets renamed for other reasons, the workaround (and
this comment) can go with it.

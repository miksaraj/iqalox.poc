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

4. **`print` and `concat` are promoted to ordinary builtin functions**, not
   statement keywords — but **called without parentheses**: `print x`,
   `concat [a, b]`. This is a fixed, non-negotiable convention (corrects an
   earlier draft of this doc, which wrongly inferred parenthesized calls by
   analogy with `add5(1)`-style user function calls — that analogy doesn't
   hold; builtins keep the bare call form from the original examples). All
   example scripts have been reverted to the bare `print x` / `concat [...]`
   form. `concat`/`print` used as a pipe target (`|> print`) is unaffected
   either way, since that's a first-class function value reference, not a
   call. `src/statement.py`'s `Print`/`Concat` statement nodes still need to
   go away once function calls exist and `print`/`concat` are registered as
   builtin function *values* instead (§4 below) — becoming a value doesn't
   imply gaining parentheses.

   **4b. Open exploration: should *no* function call require parentheses?**
   Not yet decided — flagging the real tradeoffs rather than picking one.
   It's achievable in principle (this is exactly how Haskell/ML function
   application works: juxtaposition, binding tighter than any operator), but
   it has concrete consequences for this language specifically:
   - **Compound arguments need their own grouping parens.** `fact(n - 1)`
     would become `fact (n - 1)` — the parens move from "this is the
     argument list" to "this groups a sub-expression," which look identical
     but mean different things. Simple single-token arguments (`print k`,
     already true today) are unaffected.
   - **Zero-argument calls need to stay distinguishable from bare references.**
     The language relies on referencing a function by name *without* calling
     it (`return adder`, `print fact` prints the function itself, `test(count)`
     passes `count` as a value without invoking it). If a bare identifier
     always meant "call it," that distinction is lost. Keeping `f()` as the
     explicit zero-arg call marker (even if 1+-arg calls drop parens) would
     preserve it.
   - **Multi-argument calls collide with the existing comma operator.**
     `f a, b` is ambiguous between "call `f` with two arguments" and "call
     `f` with one argument `a`, then the comma operator sequencing `, b`" —
     the comma operator is already implemented and its precedence is
     documented in the root `README.md`. Resolving this needs either
     restricting multi-arg paren-free calls, or scoping the comma operator
     out of unparenthesized call-argument position (similar to how
     `Parser.comma_as_operator` already gets toggled off while parsing a
     `[...]` vector literal).
   - **Currying is a separate, larger decision riding along with this one.**
     True Haskell-style juxtaposition implies every function is effectively
     single-argument (`f a b c` = `((f a) b) c`), which would enable partial
     application for free but changes what a function value *is* and doesn't
     match the current fixed-arity `fun f(a, b, c) {...}` declaration form.
     A fixed-arity juxtaposition (`f a b c` calls `f` with exactly the three
     declared parameters, no currying) avoids that but still needs an answer
     to the comma-operator collision above.

   Recommendation if/when this gets picked up: fixed-arity juxtaposition,
   keep mandatory `()` for zero-arg calls only, and disable the comma
   operator while parsing a paren-free argument list — but this is a
   recommendation, not a decision, and needs explicit sign-off before any
   grammar work happens here, per `CLAUDE.md`.

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
| Block comments `<# ... #>` | 🟡 | Tokens exist and scanning is wired up, but the terminator-detection loop looks buggy — see §3.1. |
| Implicit semicolons | ✅ | Newline → `SEMICOLON` token in `Scanner.scan_token`. |
| `continue` / `break` statements | ⛔ | Tokens exist (`TokenType.BREAK`/`CONTINUE`); no AST nodes, no parsing, no interpreter support. Must be usable as ternary branches per decision 1 — see §4 step 4. |
| Prefix `++`/`--` | 🟡 | Parses, but doesn't mutate yet. Needs real assign-back semantics + immutable-target error per decision 2 — see §4 step 7. |
| `extends` for inheritance | 🟡 | Token exists; no `class` declaration parsing/interpretation at all yet. |
| Chainable ternary (elvis `?:` too) instead of if/else | 🟡 | `Ternary` node, both `? :` and `?:` forms parse and evaluate for plain expressions; nesting gives chaining. Doesn't yet accept statement-like branches (`break`/`continue`) per decision 1 — see §4 step 1. |
| Support for standard methods | ⛔ | Baseline Lox method dispatch on classes (decision 6) — depends on classes existing at all. |
| No `+` string concat | 🟡 | `+` on two strings currently just does Python string concatenation in `visit_binary_expr` (`check_number_operands` isn't even invoked for `PLUS` in a string-aware way — actually `check_number_operands` *is* called for `PLUS`, so `"a" + "b"` currently raises a runtime error). So the restriction is arguably already enforced, but only as a side effect of numeric type-checking, not an intentional string-specific rule — worth an explicit test once `concat` exists as a builtin. |
| `print` / `concat` as builtin functions | ⛔ | Currently `Print`/`Concat` are dedicated `Stmt` subclasses (statement keywords), per decision 4 these need to become ordinary builtin function values instead — depends on functions/calls existing (§4 step 5) and needs its own small migration (§4 step 6). |
| Pipe operator `\|>` | ⛔ | Token exists; not parsed at all (no rule calls it). Depends on `print`/`concat` being callable values and on functions existing. |
| Ignore operator `_` | ⛔ | Token exists (`UNDERSCORE`); no parsing/semantics. Needs to work as a ternary branch per decision 1. |
| Nullable infix `??` | ✅ | Parsed in `multiplication()` (questionable precedence placement, see §3.2) and evaluated in `visit_binary_expr`. |
| Comma operator | ✅ | `comma()` in parser, precedence matches the table in the root `README.md`. |
| Immutability by default (`mut`) | ✅ | `VariableData.is_mutable`, enforced in `Environment.assign`; `var_declaration` requires either `mut` or an initializer. |

Mixin support and trait support are **out of scope for 0.1-poc** (deferred to
0.2, decision 5) and are intentionally not in this table.

Not on the 0.1-poc list but required by the example scripts and by Lox
baseline, currently **not implemented at all** (treat as in-scope per the
"assume Lox/example-script features are wanted unless they contradict an
explicit plan" rule — none of these contradict anything):

| Missing baseline | Notes |
|---|---|
| `for` loops | Grammar drafted in `langspec/SYNTAX_GRAMMAR.md`, not implemented in `parser.py`. Needed for `loops.iqx` and most of `functions.iqx`. `while` is *not* in scope — it was deliberately removed from the language (see §1). |
| Functions (`fun`, calls, `return`, closures) | No `Function`/`Call`/`Return` AST nodes, no parsing. Needed for essentially all of `functions.iqx` and for the pipe operator to be useful. |
| Classes (`class`, methods, instances, `init`, `super`, `self`) | No AST nodes, no parsing. Needed for `classes.iqx`/`inheritance.iqx`. |
| Logical `and`/`or` | Tokens exist, grammar drafted, not implemented (`loops.iqx` uses `k == 0 or k == 5`). |

## 3. Known bugs / cleanup (not design questions — just fix)

1. **Block comment terminator.** In `Scanner.scan_token`:
   ```python
   elif token == TokenType.BLOCK_COMMENT_START:
       while self.peek() != '#' and self.peek_next() != '>' and not self.is_at_end():
           self.advance()
       self.advance()
       self.advance()
   ```
   This loop exits as soon as *either* `peek() == '#'` *or* `peek_next() == '>'`
   individually — not when the two-character sequence `#>` is actually next.
   It should keep advancing while **not** (`peek() == '#' and peek_next() == '>'`),
   i.e. use `or` with negation, not `and`. As written it will terminate block
   comments early on any lone `#` or any char followed by `>`.
2. **`??` precedence.** `DOUBLE_QUESTION_MARK` is matched inside
   `multiplication()`, i.e. it currently binds as tightly as `*`/`/`/`%`/`^`.
   The root `README.md` precedence table doesn't mention `??` at all. Worth
   deciding (design call, add to §1 if you want it there) where it should
   actually sit — most languages put null-coalescing near the bottom, close
   to ternary/assignment.
3. `error.py`'s `IqaloxRuntimeError.__str__`/`__repr__` just call `super()`,
   i.e. they're no-ops — fine to leave, but not worth keeping if nobody
   relies on the override.

## 4. Suggested sequencing

With §1 resolved, all of the following can proceed without further design
sign-off — flag anything that turns up a *new* design question rather than
deciding it inline (per `CLAUDE.md`).

1. **Fix known bugs** (§3.1 at minimum — it silently corrupts anything after
   a block comment).
2. **Logical `and`/`or`** — small, unblocks `loops.iqx`.
3. **`for` loops** — per the drafted grammar (minus the removed `whileStmt`);
   needed before `break`/`continue` are meaningful.
4. **`break`/`continue`** — per decision 1, these need to work both as
   ordinary loop-body statements and as ternary branches. Implement via a
   control-flow exception (à la Lox's `return`) raised from either a
   statement position or from `visit_ternary_expr` when that branch is
   chosen, caught by the enclosing `for`'s execution loop.
5. **Functions** (`fun`, `Call`, `Return`, closures over `Environment`) —
   standard Lox mechanics; needed for almost everything else including the
   pipe operator and for `print`/`concat` to become callable.
6. **Promote `print`/`concat` to builtin functions** (decision 4) — register
   them as native function values in the global environment (rather than
   `Print`/`Concat` statement AST nodes), retire the dedicated
   `print`/`concat` statement grammar in `parser.py`, and remove `Print`/
   `Concat` from `tools/generate_ast.py`'s `STATEMENTS` dict once nothing
   depends on them. Becoming a function value does **not** mean gaining
   parentheses (decision 4) — the general `Call` grammar from step 5 (which
   presumably requires `(args)`, matching every other example call site)
   needs a paren-free calling form for at least these two builtins, e.g. a
   dedicated `builtinCall → IDENTIFIER expression` production or similar,
   independent of whatever step 5's general call syntax ends up being. If
   4b (paren-free calls generally) gets picked up later, this dedicated form
   can likely be folded into the general one.
7. **Pipe operator `|>`** — once functions and step 6 land, desugar
   `a |> f` to a call `f(a)`.
8. **Prefix `++`/`--` mutation** — per decision 2: assign the incremented
   value back to the target in `visit_unary_expr`, reject non-assignable
   operands in `increment()`, and raise on an immutable target (runtime for
   now, see decision 2 for why compile-time is deferred).
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
12. **Backfill pytest coverage** under `tests/` for scanner/parser/interpreter
    as each piece above lands, rather than after the fact.

Steps 2–3 have no dependency on anything else in this list and could start
immediately.

# Plan: getting to 0.1-poc

Status snapshot based on reading `src/` against the `0.1-poc` feature list in
`ROADMAP.md` and the example scripts in `langspec/examples/*.iqx`. This is a
living document — update the status table and the open-questions list as work
lands or decisions get made. See `CLAUDE.md` for why the open questions are
listed rather than answered here.

## 1. Open design decisions (blocking — need your call before implementing)

These aren't engineering choices; each one changes how the language behaves.
Flagging them instead of guessing, per `CLAUDE.md`.

1. **Does ternary fully replace `if`/`else`, including for statement-like
   things?** `langspec/examples/functions.iqx` and `loops.iqx` use `print k`,
   `continue`, `break`, and `_` (ignore) directly as ternary branches, e.g.:
   ```
   (k == 0 or k == 5) ? continue : (k == 10) ? break : print k
   ```
   Today, `Print` is a `Stmt`, and `continue`/`break`/`_` don't exist as AST
   nodes at all. For the examples to parse as written, either (a) these need
   to become expressions (changing `print` from a statement to an
   expression-producing builtin, and giving `continue`/`break`/`_` expression
   forms), or (b) the ternary needs to be able to take statements as
   branches, or (c) the examples are aspirational and should be rewritten
   once the real answer is chosen. Which?

2. **Prefix `++`/`--`: does it mutate?** `unary()`/`visit_unary_expr`
   currently parse `++x` as a `Unary` node and evaluate it as `float(x) + 1`
   — a pure computation with no assignment back to `x`. That's not what
   "prefix increment operator" means in the feature list, and it silently
   diverges from every example (`++i` in `for` headers, `++c |> print` in
   `createCounter`) which assume it mutates. Should `++x`/`--x`:
   - require an assignable target (a `Variable`, presumably a property access
     later) and assign the incremented value back, evaluating to the new
     value (standard prefix-increment semantics), and
   - raise a runtime error if the target is immutable (no `mut`)?

   If yes (which the examples assume), this also means `increment()` in the
   parser needs to reject non-assignable operands rather than accepting any
   unary expression.

3. **Self-reference keyword: `this` or `self`?** `src/token.py` defines
   `TokenType.SELF = 'self'` (no `this` keyword exists at all), but
   `langspec/examples/classes.iqx` uses `this.name`. One of these is wrong.
   Which keyword is canonical?

4. **Is `concat` a statement or a stdlib function value?** `src/statement.py`
   / `src/parser.py` currently implement `concat [...]` as a dedicated
   statement (`Concat`, parallel to `Print`). But the examples pipe into it
   as if it were an ordinary callable: `[a, equality, b] |> concat |> print`
   and `concat ["Factorial of ", j, "is: ", fact] |> print`. A pipe operator
   needs `concat` (and `print`, per question 1) to be values it can call —
   which a hardcoded statement keyword isn't. Do we:
   - keep `concat`/`print` as statements for 0.1-poc and treat the piped
     examples as not-yet-valid syntax to fix later, or
   - promote them to ordinary (builtin) functions now, which is also more
     consistent with `ROADMAP.md`'s 0.3 note that `concat` should eventually
     be "a standard library method" anyway?

5. **Mixins vs. traits implementation strategy.** The original notes
   explicitly leave this open: PHP-style static mixins at class declaration
   (`with`), Scala-style dynamic traits, or a mix (PHP-style for `with` at
   declaration, Scala-style for dynamic use via `use`). `module`, `trait`,
   `use`, `with`, and `extends` tokens already exist; no grammar or semantics
   are implemented yet. Needs a decision before any grammar work here starts.

6. **"Support for standard methods"** (from the original notes, kept
   verbatim in `ROADMAP.md`) is ambiguous: built-in methods on primitive
   values/arrays (e.g. calling `.length()` on a string), or just "class
   instance methods work" (i.e. the baseline Lox method-dispatch mechanics)?
   Given getters/setters are separately marked as pushed out to 0.2+, my
   guess is this means the latter (plain method definitions/dispatch on
   classes) — but confirm before treating any primitive `.method()` call
   syntax as in-scope for 0.1-poc.

## 2. Status of 0.1-poc feature list

Legend: ✅ done · 🟡 partial/buggy · ⛔ not started

| Feature | Status | Notes |
|---|---|---|
| Array support (vectors) | 🟡 | `Vector` expression + `visit_vector_expr` work for literals (`[1, 2, 3]`). No indexing, no mutation, no stdlib methods. Matrices are explicitly 0.2 scope. |
| Block comments `<# ... #>` | 🟡 | Tokens exist and scanning is wired up, but the terminator-detection loop looks buggy — see §3.1. |
| Implicit semicolons | ✅ | Newline → `SEMICOLON` token in `Scanner.scan_token`. |
| `continue` / `break` statements | ⛔ | Tokens exist (`TokenType.BREAK`/`CONTINUE`); no AST nodes, no parsing, no interpreter support. Depends on open question 1. |
| Prefix `++`/`--` | 🟡 | Parses, but doesn't mutate — see open question 2. |
| `extends` for inheritance | 🟡 | Token exists; no `class` declaration parsing/interpretation at all yet. |
| Chainable ternary (elvis `?:` too) instead of if/else | ✅ | `Ternary` node, both `? :` and `?:` forms parse and evaluate; nesting via recursive `expression()` calls gives chaining. |
| Support for standard methods | ⛔ | Depends on classes existing at all, and on open question 6. |
| No `+` string concat | 🟡 | `+` on two strings currently just does Python string concatenation in `visit_binary_expr` (`check_number_operands` isn't even invoked for `PLUS` in a string-aware way — actually `check_number_operands` *is* called for `PLUS`, so `"a" + "b"` currently raises a runtime error). So the restriction is arguably already enforced, but only as a side effect of numeric type-checking, not an intentional string-specific rule — worth an explicit test once `concat` (open question 4) is settled. |
| Pipe operator `\|>` | ⛔ | Token exists; not parsed at all (no rule calls it). Depends on open question 4 (need callable `concat`/`print` for the example scripts to make sense) and on functions existing (§2, functions row). |
| Ignore operator `_` | ⛔ | Token exists (`UNDERSCORE`); no parsing/semantics. Depends on open question 1. |
| Nullable infix `??` | ✅ | Parsed in `multiplication()` (questionable precedence placement, see §3.2) and evaluated in `visit_binary_expr`. |
| Mixin support | ⛔ | Depends on open question 5. |
| Trait support | ⛔ | Depends on open question 5. |
| Comma operator | ✅ | `comma()` in parser, precedence matches the table in the root `README.md`. |
| Immutability by default (`mut`) | ✅ | `VariableData.is_mutable`, enforced in `Environment.assign`; `var_declaration` requires either `mut` or an initializer. |

Not on the 0.1-poc list but required by the example scripts and by Lox
baseline, currently **not implemented at all**:

| Missing baseline | Notes |
|---|---|
| `for` loops | Grammar drafted in `langspec/SYNTAX_GRAMMAR.md`, not implemented in `parser.py`. Needed for `loops.iqx` and most of `functions.iqx`. |
| Functions (`fun`, calls, `return`, closures) | No `Function`/`Call`/`Return` AST nodes, no parsing. Needed for essentially all of `functions.iqx` and for the pipe operator to be useful. |
| Classes (`class`, methods, instances, `init`, `super`) | No AST nodes, no parsing. Needed for `classes.iqx`/`inheritance.iqx`. |
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

Once §1 is resolved:

1. **Fix known bugs** (§3.1 at minimum — it silently corrupts anything after
   a block comment).
2. **Logical `and`/`or`** — small, unblocks `loops.iqx`, no open design
   question attached.
3. **`for` loops** — per the drafted grammar; needed before `break`/`continue`
   are meaningful.
4. **`break`/`continue`** — once question 1 is answered, implement as
   statements or expressions accordingly (typically via a control-flow
   exception caught by the loop, à la Lox's `return`).
5. **Functions** (`fun`, `Call`, `Return`, closures over `Environment`) —
   standard Lox mechanics; needed for almost everything else including the
   pipe operator.
6. **Pipe operator `|>`** — once functions exist and question 4 (concat/print
   as values) is settled, desugar `a |> f` to a call.
7. **Prefix `++`/`--` mutation** — once question 2 is answered, fix
   `visit_unary_expr` (and `increment()` if the operand must be assignable).
8. **Ignore operator `_`** — once question 1 is answered.
9. **Classes** — `class`, `extends`, `init`, methods, `super` — once question
   3 (`this`/`self`) is answered. Getters/setters explicitly stay out of
   scope (0.2+).
10. **Mixins/traits** — once question 5 is answered; likely the largest
    single chunk of remaining grammar work.
11. **Sync `langspec/SYNTAX_GRAMMAR.md` and `langspec/README.md`** with
    whatever actually got built (the grammar doc currently doesn't even
    have array/vector syntax, `mut`, or the elvis form).
12. **Backfill pytest coverage** under `tests/` for scanner/parser/interpreter
    as each piece above lands, rather than after the fact.

Steps 2–3 and 5 have no open design dependency and could start immediately;
everything else is gated on the corresponding item in §1.

# Plan: getting to 0.2

This is the `0.2` counterpart to `docs/PLAN-0.1.md`: a living plan for the
next feature set on top of `0.1`'s compiler frontend (F#, `compiler/`) and
bytecode VM backend (C++23, `vm/`). Like the `0.1` plan, this document
tracks resolved decisions, flags open ones rather than guessing at them
(per `CLAUDE.md`), and keeps a running status table and sequencing
checklist as work lands.

`0.1-poc` (`poc/`) is **not** getting a `0.2`. It stays frozen at its own
feature set as a historical reference implementation (`CLAUDE.md`); every
feature below targets `compiler/`+`vm/` only. See §2 for what that implies
for `langspec/examples/` and the Phase 9 conformance suite.

## 0. Scope: what "0.2" means here

Per `ROADMAP.md`'s `0.2` entry (formerly `0.2-poc`, retired as a separate
PoC stage and folded in as the natural home for "class system
completeness" work):

- Matrix support (alongside existing vector/array support)
- Array-manipulation standard library, including list comprehensions and
  Julia-inspired array manipulation
- Anonymous function literals (lambdas) — distinct from the named
  nested-function closures `0.1` already has
- Variadic unpacking (`...`)
- Getters and setters — resolved this round as **`pub`/`mut` visibility
  and mutability modifiers**, not custom accessor bodies (a real
  mid-review redesign — see §1)
- Mixin support (`with`) and trait support (`trait`/`use`) — resolved this
  round as a PHP-style/Scala-style split (§1)

## 1. Design decisions (resolved)

Resolved across this planning round and a PR review pass on the first
draft (2026-07-06 to 2026-07-07) — recorded with the reasoning, not just
the answer, per this project's convention. A few items below are real
revisions to what an earlier draft of this document said (one outright
reversal, one full redesign, one genuinely new extension) rather than
incremental refinements — each says so explicitly rather than silently
overwriting the earlier text.

1. **Lambda syntax is `(a, b) -> expr`** — arrow syntax, single-expression
   body only (no block body, no `return`; anything needing statements
   stays a named `fun`). No new keyword; `Minus` immediately followed by
   `Greater` scans as a new `Arrow` token, the same two-character-lookahead
   pattern `Scanner.fs` already uses for `->`'s siblings (`??`, `?:`,
   `++`, `--`).
2. **Cons (`[item | list]`) and list comprehensions share `[expr |`, told
   apart by lookahead on the generator marker.** After `|`, if what
   follows matches a generator clause (`x <- xs`), it's a comprehension;
   a bare expression makes it cons. `[x | xs]` conses `x` onto `xs`;
   `[x | x <- xs]` comprehends. `xs` in the cons case must be a vector at
   runtime (a runtime type error otherwise, matching how every other
   vector operation reports a bad-type argument).
3. **List comprehensions ship with a single generator, no guards, for
   `0.2`.** `[expr | pattern <- iterable]` only. Multiple comma-separated
   generators (nested/Cartesian iteration) and boolean guards are the
   *eventual* shape (the original `[x + y | x <- xs, y <- ys, x not y]`-
   style sketch in `ROADMAP.md`) but explicitly not this pass — now
   tracked as its own line item on `ROADMAP.md`'s `0.3` entry rather than
   just a mention here, so it doesn't quietly fall off the plan.
4. **The generator token is `<-`** (Haskell-style), not `in`. Unambiguous
   since nothing else in the grammar uses it; a new `LeftArrow` token.
5. **Matrices are nested vectors (`[[1, 2], [3, 4]]`), not a new literal
   syntax.** No Julia-style space-separated-row literal (`[1 2; 3 4]`) —
   that would make whitespace significant inside `[...]` for the first
   time in the grammar, a much bigger change than the feature needs. A
   matrix is a vector of vectors with dedicated stdlib functions (multiply,
   transpose, elementwise ops — exact list still open, §2) recognizing the
   shape; Julia-flavored *API*, not Julia-flavored *literal syntax*.
6. **Indexing ships as `v[i]` read/write only, 0-based, no negative
   indices, no slicing.** `v[i]` and `v[i] = x`; out-of-range is a runtime
   error, matching every other bounds/type check's error style. Negative
   indices and slice syntax (`v[a:b]`) are deferred to `ROADMAP.md`'s
   `0.3` entry ("array-manipulation standard library improvements"),
   named explicitly there now rather than just implied. A non-integer or
   negative-*numeric* index (there being no separate integer type,
   `v[1.5]`) is also a runtime error, matching every other arithmetic/
   domain check's error style — an obvious edge case decided the same way
   as every other operator's type checking, not a fresh design axis.

   **Addendum, found during Phase 1 implementation, not caught by this
   document's original grammar draft or `langspec/SYNTAX_GRAMMAR.md`'s
   Phase 0 rewrite: `v[0]` is byte-for-byte ambiguous with `0.1`'s
   existing "bare identifier/property + vector-literal argument" call
   syntax** (`concat [1, 2]`, used throughout every existing example) —
   both parse to the identical token stream (`IDENTIFIER LEFT_BRACKET ...
   RIGHT_BRACKET`), so `Parser.fs` can't tell "index `v` at 0" from "call
   `v` with the one-element vector argument `[0]`" without a tiebreaker.
   Resolved via **whitespace adjacency**: a `[` with no space before it
   (`v[0]`) is always postfix indexing; a `[` preceded by a space
   (`v [0]`, `concat [1, 2]`) is always the pre-existing vector-literal-
   argument call, completely unchanged. Uses `Token.Column`/`Lexeme`
   (already tracked since the column-tracking work predating `0.2`) — no
   new scanner state needed. This is the first place whitespace is
   *ever* significant in Iqalox's grammar; flagged here explicitly since
   it's a real language-design tradeoff, not an implementation detail,
   and the tradeoff was made by necessity (the alternative was a breaking
   change to `0.1`'s call syntax) rather than by preference.
7. **Variadic unpacking (`...`) ships as vector-literal spread only:
   `[...a, ...b]`.** Splices `a`'s and `b`'s elements into the new vector.
   Variadic *parameters* (`fun f(...args)`) and call-site spread
   (`f(...someVector)`) are explicitly **out of scope** for `0.2` — real
   tension with `0.1`'s stated design ("a call's arity is fixed by the
   callee's declaration, not inferred from how many arguments happen to be
   written") that wasn't asked for or resolved this round. Both are now
   logged under `ROADMAP.md`'s new "Language feature ideas under
   consideration" section rather than dropped from sight.
8. **Getters/setters are scrapped in favor of `pub`/`mut` visibility and
   mutability modifiers — a real, mid-review redesign, not an incremental
   tweak to the original forced-accessor idea.** Properties are declared
   in the class body with `var`-style declarations, extending the exact
   same keyword `0.1` already uses for local/global variables into a new
   grammar position (class bodies currently only accept method
   declarations; `0.2` adds property declarations alongside them):
   - `var name` — private, immutable.
   - `var name mut` — private, mutable.
   - `var name pub` — public, immutable.
   - `var name pub mut` — public, mutable.

   No new keyword needed for `mut` (already `Token.fs`'s `Mutable`,
   reused as-is); `pub` is the one genuinely new keyword this decision
   needs. This directly foreshadows `0.5`'s real module support: `pub` is
   expected to apply to classes themselves then too (private-by-default at
   the module level, defining what a module actually exports) — called
   out explicitly on `ROADMAP.md`'s `0.5` entry now so it isn't forgotten.

   **Necessary consequence, not separately asked about: `0.1`'s "a field
   springs into existence on first assignment" model is gone.** Decision
   10's access table only makes sense if `self.x`/`instance.x` always
   refers to a *declared* property with known `pub`/`mut` flags to check
   against — an undeclared, implicitly-created field would have no
   visibility/mutability metadata at all, silently bypassing the whole
   mechanism. So every property a `0.2` class uses must have a `var`
   declaration somewhere in the class body; a bare `self.x = value` with
   no matching declaration is a compile-time error, the same category of
   error as assigning to an undeclared local. This is inferred from
   decision 10's own internal logic rather than a separate, explicit
   answer — flag it if declared-only state wasn't actually intended.
9. **Property immutability is enforced at runtime, "assign at most once,
   ever" — not compile-time, and not "once per instance is fine as long as
   it's early."** `0.1`'s local-variable immutability is a compile-time
   check because `Resolver.fs` can statically enumerate every assignment
   site for a given local. A class property assigned via `self.x = value`
   scattered across arbitrarily many methods (and, once inheritance is
   involved, subclass methods too) isn't nearly as tractable to verify
   statically, so this extends the same mechanism `0.1` already has for
   `undef`: an immutable property starts unset, transitions to set on its
   first assignment, and any assignment after that is a runtime error —
   from anywhere, internal or external. This also resolves a real gap in
   reusing `var`'s exact rule literally: an immutable local `var x` with
   no initializer is already a **parse error** in `0.1` ("no way to ever
   give it a value") — but a class property commonly gets its value from
   a constructor parameter, not a fixed literal at the declaration site.
   Property declarations therefore need their own, more permissive rule:
   no initializer required at declaration; the first assignment (wherever
   it happens to occur) sets it, and reading it before that is a runtime
   error, the same shape `0.1`'s `undef` already has for `var mut` locals
   — just without needing `mut` for that leniency.
10. **Access is gated by an internal/external boundary, the same rule for
    both `pub` and `mut`.** Any method belonging to the class — or,
    pending §2.3's confirmation, a subclass — can freely read/write `self.x`
    regardless of `pub`/`mut`, exactly like `0.1`'s fields work internally
    today; only access via an *external* reference (`instance.x` from
    outside the class) is gated by the declared modifiers:

    | Declaration | Internal (`self.x`) | External (`instance.x`) |
    |---|---|---|
    | `var x` | write once, then read-only | invisible |
    | `var x mut` | read/write, unlimited | invisible |
    | `var x pub` | write once, then read-only | read-only |
    | `var x pub mut` | read/write, unlimited | read/write |

    No accessor bodies, no custom logic on get/set — external access to a
    `pub` property is a direct read (or, for `pub mut`, read/write) of the
    same backing value internal access already uses, subject to the
    mutability check above. This is considerably simpler than the
    get/set-block design an earlier draft of this document had; there's
    no design work needed here for "what does a getter return," "can a
    setter have side effects," etc., because there's no user-written
    accessor logic at all.
11. **Methods are private by default too, using the same `pub` modifier —
    a real breaking change to every existing example script, not
    additive.** A method declared without `pub` (`quack() { ... }`) is
    only callable from within the class (or, pending §2.3, a subclass);
    calling it from outside is an error. `init` is always implicitly
    callable from outside regardless of any `pub` annotation — a
    completely unconstructable class isn't a sensible default, and
    nothing in this round's decisions suggested wanting one. Every
    existing `langspec/examples/*.iqx` method call from outside its own
    class (e.g. `duck.quack()`) needs an explicit `pub quack() { ... }`
    from `0.2` onward. Flagged prominently in §7 — this is discovered and
    named now, not found the hard way partway through Phase 7.
12. **Mixins/traits split by *which keyword*, matching each keyword's own
    etymology — corrected from this document's first draft, which had it
    backwards.** `trait T { ... }` composed via `use` is **PHP-style
    static copying** (PHP's own `trait`/`use` keywords do exactly this);
    a separate, plainer `with`-only mixin form (`class C extends Base
    with M1, M2 { }`) is **Scala-style dynamic linearization** (Scala's
    own `with` keyword does exactly that). Concretely: `use`d traits'
    members are copied in once at class-declaration time, extending the
    same static method-table-copy mechanism `0.1`'s `extends` already
    uses, no runtime dispatch chain involved; `with`-listed mixins compose
    via an ordered linearization chain (exact algorithm still open, §2),
    closer to real multiple inheritance. Both forms are usable "anywhere"
    per the original answer, but see §2 for why `0.2` can only actually
    deliver the class-scoped case (module-level composition needs
    `module` itself, which isn't real until `0.5` — now called out
    explicitly on `ROADMAP.md`'s `0.5` entry).
13. **`langspec/` versioned-snapshot subdirectory is `langspec/versions/<version>/`**
    (PR review, resolving the naming-collision question raised in this
    document's first draft). The current version's grammar docs and
    examples stay at `langspec/`'s top level as always; once a version is
    superseded, its snapshot moves to `langspec/versions/<version>/` (e.g.
    `langspec/versions/0.1/`) rather than reusing the bare version number,
    which would collide with (or be confused for) the pre-existing
    `langspec/archived/<version>/` directories — those hold unrelated,
    pre-renumbering *planning-era* snapshots per `ROADMAP.md`'s own
    renumbering note, and keep their existing name/meaning unchanged.

## 2. Open questions (flagged, not decided)

Per `CLAUDE.md`, these block the phases that depend on them (noted per
item) — not the whole plan. Add to this list rather than silently
resolving, the same convention `docs/PLAN-0.1-POC.md` and
`docs/PLAN-0.1.md` §2 already used.

1. **Static (`trait`/`use`) conflict resolution.** If two `use`d traits
   (or a trait and the class's own superclass) define the same member
   name, what happens? PHP itself requires explicit `insteadof`/`as`
   conflict resolution rather than silently picking one — does `0.2` need
   the same, or is last-`use`d-wins (matching a flat, ordered copy)
   acceptable? Blocks Phase 8.
2. **Dynamic (`with`) linearization algorithm.** Scala's own `with` uses
   C3 linearization over the full inheritance graph to resolve conflicts
   and `super`-style chaining among mixed-in traits. Does `0.2` need the
   full algorithm, or a simpler ordered-fallback approximation for a first
   pass? Blocks Phase 8.
3. **Does privacy (properties *and* methods) extend to subclasses, or stop
   at exactly the declaring class?** Decision 10/11's internal/external
   line is clear for "inside this class" vs. "outside entirely," but
   doesn't say whether a subclass's own methods count as internal to the
   superclass's private members (a `protected`-like reading) or whether
   privacy is stricter than that (a subclass is just another external
   caller as far as the superclass's private state is concerned). Also
   determines whether a private method even participates in dynamic
   dispatch/overriding at all, or is effectively non-virtual since no
   external caller can ever reach it polymorphically. Blocks Phase 7
   (properties) and its methods-privacy extension alike.
4. ~~Exact array-manipulation stdlib surface.~~ **Resolved during Phase 5**
   (see its own entry in §5): `length`, `push`, `pop`, `reverse`, `map`,
   `filter`, `reduce`, `sort` — all plain global functions, not methods
   (`0.1`'s object model has no method-dispatch-on-primitives concept at
   all). `push`/`pop` mutate in place; the rest return a new vector.
5. **Exact matrix stdlib surface.** Multiply, transpose, elementwise
   arithmetic are the obvious Julia-flavored candidates — full list not
   yet confirmed. Blocks Phase 6.
6. **What happens to the Phase 9 conformance suite once `langspec/
   examples/` moves to `0.2` syntax.** `poc/` is frozen and can't parse
   `0.2`'s new syntax at all — once the *current*, top-level
   `langspec/examples/*.iqx` stops being `0.1-poc`-compatible,
   `scripts/conformance-test.sh` can no longer diff `poc/` output against
   the live top-level examples the way it does today. It would need to
   point specifically at `langspec/versions/0.1/` (decision 13) for the
   `poc/`-vs-`compiler/`+`vm/` comparison, while the *current* top-level
   examples become a `compiler/`+`vm/`-only smoke test with no `poc/`
   counterpart at all. Blocks Phase 9.

## 3. Grammar and architecture additions (overview)

No new implementation stack this time — everything below extends the
existing `compiler/`+`vm/` pipeline from `docs/PLAN-0.1.md` §3.

**New tokens** (`compiler/src/Token.fs`/`Scanner.fs`): `Arrow` (`->`),
`LeftArrow` (`<-`); `Ellipsis` (`...`, already scanned since `0.1-poc`,
just never given grammar until now); a new `pub` keyword. `mut` needs no
new token — `Token.fs`'s existing `Mutable` (already used by `var x mut`)
is reused as-is for property declarations.

**New AST/Bound node shapes** (`Ast.fs`/`Bound.fs`): an `Index`/`IndexSet`
expression pair (parsed as a postfix `[...]` on any primary, distinct from
a vector *literal*'s leading `[...]`); a `Lambda` expression (parameter
list + single body expression, resolved exactly like a named function's
parameter list/closure capture, just with no name to bind); a `Cons`
expression and a `ListComprehension` expression (both `[...]`-delimited,
told apart per decision 2's lookahead); a `PropertyDecl` class-member
shape alongside the existing `FunctionDecl` (name, `pub`/`mut` flags, no
body — contrast with the get/set-block shape an earlier draft of this
document had); `pub` as an optional flag on `FunctionDecl` itself, for
method visibility; `with`-mixin and `trait`/`use` composition on
`ClassStmt`.

**`Resolver.fs`**: lambdas resolve like an anonymous `FunctionDecl` (same
scope/slot/upvalue machinery `0.1` already has for nested named
functions); `self.x` vs `instance.x` needs a genuinely new concept this
version — "is this access happening inside a method of the class (or,
pending §2.3, a subclass) the member belongs to" — to implement decision
10/11's internal/external split, since `0.1`'s `GetProperty`/
`SetProperty`/method-call resolution never distinguished internal from
external access at all; mixin/trait member resolution happens at
class-declaration time, extending the existing superclass-method-table-copy
mechanism (`vm/src/vm.cpp`'s `Inherit` opcode handler) to also fold in
`use`d traits' members (flat copy) or `with`-listed mixins' members
(linearized chain, pending §2.2's algorithm question).

**`Codegen.fs`**: new opcodes for indexed get/set (bounds-checked at
runtime, matching the existing `GetProperty`/`SetProperty` error-reporting
style); `Closure` already covers lambdas with no new opcode needed, since
a lambda is just a nameless `FunctionConstant`; cons compiles to
build-a-new-vector-by-prepending (no dedicated opcode either — expands to
existing `BuildVector` plus copying the tail vector's elements); a list
comprehension desugars to a loop that builds a vector via repeated
`BuildVector`-equivalent pushes, closer to how a `for` loop already
compiles than to anything genuinely new; vector-literal spread
(`[...a, ...b]`) is a `BuildVector`-time flattening step; `GetProperty`/
`SetProperty`/method-call opcodes gain a visibility + (for properties)
an assigned-once check, but need no accessor-invocation logic at all,
per decision 10's simplification.

**`vm/`**: `ObjVector` gains bounds-checked indexed get/set (it already
has a `std::vector<Value> elements` — indexing needs no new heap-object
shape, just new opcode handlers); `ObjInstance`'s `fields` map gains a
per-field "has this been assigned yet" bit for immutability enforcement
(decision 9) plus whatever visibility metadata `GetProperty`/`SetProperty`
need to check decision 10's internal/external rule; mixin/trait
composition extends `Vm::run`'s existing `Inherit` handling.

## 4. Feature checklist (parity target)

Every row starts "not started" — ticked off as `compiler/`+`vm/` land each
one, verified via new tests (§6) and, where a `poc/`-parity fixture still
applies, the existing conformance suite. **Learned from `0.1`'s own Phase
10 audit: keep this table current as work actually lands, don't let it go
stale for nine phases and then need a special pass to fix it.**

| Feature | Design decision(s) | Status |
|---|---|---|
| Vector indexing (`v[i]` get/set) | §1.6 | Done |
| Lambdas (`(a, b) -> expr`) | §1.1 | Done |
| Cons operator (`[item \| list]`) | §1.2 | Done |
| List comprehensions (single generator) | §1.2-4 | Done |
| Vector-literal spread (`[...a, ...b]`) | §1.7 | Done |
| Array-manipulation stdlib | §2.4 | Done |
| Matrices (nested vectors + stdlib) | §1.5, §2.5 (list not yet final) | Not started |
| Property `pub`/`mut` modifiers | §1.8-10, §2.3 | Not started |
| Method `pub`/private | §1.11, §2.3 | Not started |
| Mixins (`with`, dynamic linearization) | §1.12, §2.2 | Not started |
| Traits (`trait`/`use`, static copy) | §1.12, §2.1 | Not started |

## 5. Suggested sequencing

A proposed order, not a commitment — reorder freely. Indexing first since
almost everything else either needs it directly (matrices, array stdlib)
or benefits from the `[...]`-postfix grammar work being already proven
before cons/comprehensions reuse the same bracket. Properties/methods
visibility and mixins/traits pushed later since they're the largest, most
self-contained (class-system-only) changes and don't block the
array/stdlib work at all — feel free to swap their order if you'd rather
tackle the biggest risk first instead of last.

**Phase 0 — `langspec/` versioning move.** *Done.* Moved `0.1`'s
`langspec/` snapshot (grammar docs + examples) into `langspec/versions/0.1/`
(decision 13) — mirroring `docs/PLAN-0.1.md`'s own Phase 0 (repository
reorganization) pattern of doing the structural move first, separately
from any feature work. A light accuracy pass came first (the pre-move
`langspec/README.md`/`SYNTAX_GRAMMAR.md` still described `0.1-poc`, not
the real, shipped `0.1` — `0.1`'s four additions over `0.1-poc` are
semantic, not grammatical, so the BNF itself needed no changes, just the
prose). Per owner request, this phase also went further than the move
alone: the new top-level `langspec/SYNTAX_GRAMMAR.md`, a repurposed
`langspec/README.md` (now a directory-navigation guide rather than a
version-specific spec), and a full `langspec/examples/*.iqx` set
exercising every decision in §1 were all written now, ahead of Phases
1-8 actually implementing any of it — a deliberate spec-first departure
from this document's original Phase 9 framing ("the current, `0.2`-syntax
top-level examples become a `compiler/`+`vm/`-only check" was written
assuming that wiring would happen at the *end*). To keep CI green through
Phases 1-8, `scripts/conformance-test.sh`, `scripts/phase7-run-smoke-test.sh`,
the CI `poc` job, and `.github/workflows/release.yml`'s example-bundling
step all point at `langspec/versions/0.1/examples/` for now (verified:
both scripts still pass against it) — flipping back to the top-level
`langspec/examples/` once `0.2` fully lands remains Phase 9's job. One
necessary-but-unstated consequence surfaced while writing property
examples: decision 8's addendum (§1) now spells out that undeclared,
implicitly-created fields (`0.1`'s model) can't coexist with the
pub/mut access table, so `0.2` requires every property to be declared.

**Phase 1 — Indexing.** *Done.* `v[i]` read/write, bounds-checked,
0-based, postfix on any primary expression (`f()[0]`, chained `grid[i][j]`,
all compose the same way `.property` access already does) --
`Ast.Index`/`IndexSet` through `Bound.BIndex`/`BIndexSet` to two new
no-operand opcodes, `GetIndex`/`SetIndex` (the index is a runtime stack
value, not a compile-time constant like `GetProperty`'s name). Found and
resolved a real grammar collision along the way that this document's
original draft missed entirely — decision 6's addendum (§1) covers the
whitespace-adjacency disambiguation between `v[0]` (indexing) and `concat
[1, 2]` (the pre-existing vector-literal-argument call syntax, unchanged).
11 new xUnit tests (parser disambiguation/chaining, resolver, codegen
instruction sequences) and 7 new Catch2 tests (happy path, out-of-range,
negative, non-integer, non-number-index, non-vector-receiver for both
`GetIndex`/`SetIndex`). Also surfaced (not fixed — out of scope here) a
real, pre-existing `0.1` scanner limitation while validating
`langspec/examples/matrices.iqx` end to end: every newline scans as an
implicit `;` with no bracket-depth awareness at all, so a multi-line
vector literal (or any multi-line grouping expression) has never actually
worked — logged as `docs/LANGUAGE.md` §13 item 9; `matrices.iqx`'s matrix
literal was rewritten onto one line to work around it.

**Phase 2 — Lambdas.** *Done.* `(a, b) -> expr`, single-expression body,
closing over enclosing scope exactly like a named nested function already
does -- `Ast.Lambda` desugars in `Resolver.fs` to an ordinary, nameless
`FunctionDecl` with a single implicit `return` statement, then resolves
via the *unchanged* `ResolveFunction`, so scope/slot/upvalue capture (and
parameter immutability) come for free. `Codegen.fs` needed no new opcode
at all: `BLambda` just calls the existing `CompileFunctionValue` (already
factored apart from "then bind it to a name" for exactly this kind of
reuse), pushing a `Closure` the same way a named function or method
already does. `Token.fs`/`Scanner.fs` needed one addition (`Arrow`, `->`)
via the scanner's existing longest-match operator table -- no bespoke
lookahead there either.

Disambiguating `(a, b) -> expr` from `0.1`'s pre-existing grouped comma
expression `(a, b)` (same opening token, same shape) needed one new
parser helper, `isLambdaAhead`: a pure lookahead (no backtracking) that
scans for a bare, comma-separated identifier list immediately followed by
`) ->` before committing to either parse path.

Also found, while writing tests, a pre-existing `0.1` call-grammar
limitation unrelated to lambdas themselves: a non-identifier callee can
never be called, with or without parens (`(f) 5` and even `(f)()` both
fail to parse) -- `CallHead()`'s "does an argument-shaped token follow"
check only ever runs for a bare identifier. Not new, not fixed here, but
newly relevant now that lambdas make "produce a callable value inline" a
natural thing to reach for immediately-invoked-function-style; logged as
`docs/LANGUAGE.md` §13 item 10.

18 new xUnit tests: 3 scanner (`Arrow` distinct from `Minus`/`MinusMinus`),
8 parser (single/multi/zero-parameter lambdas, the grouped-comma-expression
non-lambda case, a lambda as a call argument, curried lambdas), 4 resolver
(parameter locals, upvalue capture, parameter immutability), 1 codegen
(instruction-level `Closure`/implicit-`return` shape). No VM/C++ changes
needed at all, so the existing 58 Catch2 tests, the smoke test, and the
conformance suite all stay untouched and green.

**Phase 3 — Cons and list comprehensions.** *Done.* `[item | list]`,
`[expr | pattern <- iterable]` (§1.3's single-generator, no-guards slice).
Both share `Ast.fs`'s pre-existing `[...]` bracket syntax with plain vector
literals and each other, disambiguated in `Parser.fs`'s `Primary()` purely
by lookahead after a bare `|` (decision 2): an identifier immediately
followed by `<-` means a comprehension, anything else means cons.
`Token.fs`/`Scanner.fs` needed two new single/two-character tokens
(`VerticalBar`, `LeftArrow`), both already distinct from their nearest
lookalikes (`Pipe`'s `|>`, `Less`/`LessEqual`) via the scanner's existing
longest-match table -- no bespoke lookahead there either.

Both constructs need a real runtime loop over a vector whose length isn't
known at compile time, which rules out compiling them as a fixed-operand
`BuildVector`. Two new no-operand opcodes support that loop:
`VectorLength` (pop a vector, push its element count, a runtime type error
otherwise) and `VectorAppend` (pop a value and a vector, `push_back` the
value into the vector's own heap-allocated element storage, push nothing
back -- the mutation is visible through every other reference to the same
`ObjVector*` for free, without ever needing to store the vector back
anywhere).

**A real design bug was found and fixed during implementation**, not
caught by this document's original draft: the first attempt had
`Resolver.fs` allocate hidden accumulator/index locals (`$result`,
`$index`) directly in the *enclosing* scope, the same way an ordinary
`var` gets a slot. That works when a `Cons`/`ListComprehension` sits at
statement level (e.g. a `var` initializer) but silently corrupts stack
addressing the moment one appears **mid-expression** -- e.g. as a call
argument (`print [1 | []]`) -- because `Resolver.fs`'s static slot count
has no visibility into transient values `Codegen.fs` pushes mid-expression
(the callee itself, in that example), which already occupy stack positions
the hidden locals' slot numbers didn't account for. Found via manual
end-to-end testing (`var squares = [n * n | n <- ...]` worked; `print [1 |
[]]` corrupted an unrelated global lookup into a runtime type error) rather
than by any unit test, since every test at the time exercised these
constructs only at statement level.

Fixed by discarding the hidden-local design entirely and desugaring both
constructs, in `Resolver.fs`, into a call of a synthetic, nameless closure
-- exactly `Lambda`'s own desugaring (Phase 2), reused wholesale: `item`/
`list` (or `source`, for a comprehension) are evaluated once, in the
*enclosing* scope, and passed in as ordinary call arguments; everything
the loop itself needs lives in the synthetic closure's own fresh
`FunctionState`/`FunctionContext`, whose slot numbering is completely
decoupled from whatever the enclosing expression already has on the stack
-- the same isolation any nested closure already gets. Extracting the
loop's final accumulator value out from under its own now-discarded locals
falls out for free from `Return`'s existing native pop/truncate/push
mechanism, needing no new opcode of its own. (An interim opcode,
`PopNKeepTop`, was added and then fully removed once this became clear --
never shipped, so it left nothing behind to migrate.) Two new internal-only
`Ast.Expr`/`Bound.BoundExpr` node pairs
(`InternalVectorLength`/`InternalVectorAppend` -> `BVectorLengthInternal`/
`BVectorAppendInternal`) wrap `VectorLength`/`VectorAppend` for the
synthetic closure's own body to call -- neither has any surface syntax of
its own; `Resolver.fs` is the only place that ever synthesizes them.

A comprehension's bound variable (`x` in `x <- xs`) is declared fresh
inside the loop body's own block scope each iteration, exactly like any
other block-scoped `var` -- so it correctly shadows an enclosing variable
of the same name without corrupting it, and its slot is correctly reused
iteration to iteration. Both `item`/`body` are resolved *within* the
synthetic closure, so a reference to any other enclosing-scope name
correctly captures it as an upvalue, exactly like a lambda body would
(verified: a comprehension inside a function body correctly closes over
that function's own locals).

14 new xUnit tests (2 scanner: `VerticalBar`/`LeftArrow` distinct from
`Pipe`/`Less`/`LessEqual`; 6 parser: cons vs. comprehension vs. plain
vector-literal disambiguation, nesting, arbitrary comprehension sources; 6
resolver: the desugared `BCall(BLambda(...), args)` shape, enclosing-scope
argument evaluation, upvalue capture from within the synthetic closure's
body, comprehension-variable shadowing, internal-primitive usage) and 2
codegen (instruction-level shape of both desugared calls, including the
full loop body for cons). 7 new Catch2 tests (`VectorLength`/`VectorAppend`
happy path and non-vector runtime errors, cross-reference aliasing, and two
full end-to-end hand-assembled reproductions of the exact bytecode
`Codegen.fs` emits for cons -- including the originally-failing `[1 | []]`
call-argument case, now passing). `langspec/examples/cons_and_comprehensions.iqx`
verified end to end through the real `iqaloxc`+`iqaloxvm` toolchain.

**Phase 4 — Vector-literal spread.** *Done.* `[...a, ...b]` (decision 7).
No new tokens needed -- `Ellipsis` (`...`) has been scanned since
`0.1-poc`, just never given grammar until now. A new `Ast.Spread`/
`Bound.BSpread` expression pair wraps the spread-away inner expression;
`Parser.fs` recognizes a leading `...` on any vector-literal element, and
-- since decision 7 scopes spread to vector literals only -- a *leading*
spread on the very first element unambiguously rules out the existing `|`
lookahead entirely, so `[...a | b]` is a plain syntax error rather than
something ambiguously cons-or-spread-shaped.

Unlike Phase 3's `Cons`/`ListComprehension`, spread needed **no synthetic
closure and no hidden locals at all**. A spread-free vector literal
compiles exactly as before (`BuildVector n`, one fixed operand); once any
element is a spread, `Codegen.fs` instead builds the vector by chaining
pure stack operations: `BuildVector 0` starts an empty accumulator, then
each element either extends it with a spread source directly, or first
wraps a plain value in its own one-element `BuildVector 1` and extends
with that -- a single new opcode, `VectorExtend` (pop a source vector,
pop a target vector, append the source's elements onto the target's own
element list, and, unlike Phase 3's `VectorAppend`, **push the target back
onto the stack** so the next element in the chain can keep extending it).
Because every step both consumes and re-produces the accumulator in place
on the stack -- never in a named local slot -- there's no "declare a new
local here" step for `Resolver.fs`'s slot-counting model to get wrong, so
this sidesteps the exact mid-expression stack-corruption bug Phase 3 hit
and fixed by moving to an isolated closure frame. Verified directly:
`sum([...a, 9])` (a spread inside a call argument, the same shape
`print [1 | []]` broke under the old Phase 3 design) produces the correct
result with no isolation machinery needed at all.

A non-vector spread source (`[...5]`) is a real, user-facing runtime type
error ("Can only spread a vector, got number."), unlike `VectorAppend`'s
non-vector-receiver case (an internal-consistency check only, since that
one's never reachable from surface syntax) -- `VectorExtend`'s target,
by contrast, needs no check at all, since `Codegen.fs` only ever builds it
with `BuildVector` immediately beforehand.

10 new xUnit tests (6 parser: spread parsing, mixed spread/plain
positions, the "no spread at all" case, the leading-spread-rules-out-cons
disambiguation, an arbitrary spread source expression; 2 resolver: the
`BSpread` shape, independent resolution of mixed elements; 3 codegen:
spread-free `BuildVector` unchanged, the full flattening instruction
sequence, an all-spread literal) and 4 new Catch2 tests (`VectorExtend`
happy path, chained extends building up one target, non-vector-source
runtime error, a full end-to-end hand-assembled reproduction matching
`Codegen.fs`'s own emitted sequence for `[0, ...a, 5]`).
`langspec/examples/spread.iqx` verified end to end through the real
`iqaloxc`+`iqaloxvm` toolchain.

**Phase 5 — Array-manipulation standard library.** *Done.* Resolves §2.4:
`length`, `push`, `pop`, `reverse`, `map`, `filter`, `reduce`, `sort`,
decided live (via a design-questions round with the repository owner,
following this project's "ask rather than guess" convention) rather than
guessed from the original candidate list. Key decisions: plain global
functions, not methods on vectors (`0.1`'s object model has no
method-dispatch-on-primitives concept at all, and adding one is a much
bigger change than a stdlib phase); `push`/`pop` mutate their vector
argument in place (matching how `VectorAppend`/spread already treat
vectors as mutable heap objects regardless of the binding's own `mut`
status), the rest return a new vector; argument order is function-first
for the four that take one (`map fn, v`, matching Lisp/mathematical
convention over `push`'s own receiver-first shape); `reduce` always takes
an explicit `initial` (`reduce fn, v, initial`) rather than silently
seeding from the vector's first element; `sort` always takes an explicit
comparator (`sort fn, v`, `fn(a, b)` truthy meaning "`a` sorts before
`b`") rather than assuming a numbers-only default order, since `<`/`<=`
only accept numbers today and teaching them about strings too is out of
scope here.

An architectural split feeds directly from a technical fact checked
against the real VM before deciding anything: `Vm::callNative` invokes a
native function's C++ implementation synchronously and expects a `Value`
back immediately, while calling a closure only ever queues a new
`CallFrame` for the *existing* bytecode dispatch loop to pick up later --
there's no "call this closure and synchronously get its result" primitive
a native could use. `push`/`pop`/`length`/`reverse` need no such thing
(direct `ObjVector` element-list access, implemented as ordinary natives
in `vm/src/natives.cpp`, registered the same way as `print`/`concat` was
in Phase 7). `map`/`filter`/`reduce`/`sort` do need to call back into a
user-supplied lambda -- rather than build a new, untested VM reentrant-call
capability, they're ordinary Iqalox *source* (`compiler/src/Prelude.fs`,
a plain embedded string), textually prepended to every program's own
parsed statements (`Program.fs`) before one combined resolve/codegen
pass -- each one is a completely unremarkable `fun` declaration, using
`for`/lambda-calls/indexing/`push` exactly like any user program could,
needing zero special-casing in `Resolver.fs` or `Codegen.fs` and no new
opcodes at all. `push`/`pop`/`length`/`reverse` *are* added to
`Resolver.fs`'s `nativeGlobals` (pre-registered, immutable, matching
`print`/`concat`); `map`/`filter`/`reduce`/`sort` deliberately are not --
an ordinary top-level `fun` already resolves to a protected global on its
own, so redeclaring any of the eight is an "already declared" compile
error either way.

**A real, pre-existing parser bug was found and fixed** while writing the
prelude's own source, blocking its `map fn, v`-shaped calls specifically
when `fn` is a lambda in *non-last* argument position (verified this is
genuinely pre-existing, not something Phase 1-4 introduced, by reproducing
it against plain user-facing syntax with no prelude involved at all --
`apply (x) -> x * 2, 5` on a fresh test file was already broken before any
Phase 5 code existed). Root cause: a call argument is parsed via
`Argument()`, which (unlike `[...]`'s own element-parsing) never
suppressed the comma operator around itself -- so a lambda argument's own
*unparenthesized* body, parsed through the full `Expression()` chain,
would treat a bare comma as its own operator and silently swallow
whatever argument was meant to follow it (`map (x) -> x * 2, v` produced
a 1-argument call, not 2). Every existing Phase 2 lambda-as-call-argument
test happened to put the lambda *last*, so nothing was ever after it to
swallow. Fixed with the same `commaAsOperator` suppression `[...]`
already uses, applied around `Argument()` itself; `Primary()`'s `Grouping`
branch was given a matching, opposite fix (always *re-enabling* the comma
operator for its own parenthesized contents, restoring the outer state
afterward) so a legitimate parenthesized comma-tuple passed as a single
argument (`f (a, b)`) -- or nested inside a vector literal (`[(a, b), c]`,
previously silently broken the same way, just never noticed) -- keeps
working exactly as before. Verified via the full existing xUnit suite
(no regressions) plus new regression tests for both the bug and the fix's
own boundary (parenthesized comma-tuples still working).

Two known, accepted limitations logged rather than solved (`docs/LANGUAGE.md`
§13 items 11-12): a one-line function body ending in a bare `return x`
needs an explicit `;` before `}` (an ASI gap, found while writing the
prelude in as few lines as reasonable -- ASI only ever fires on a real
newline, ` }` doesn't imply one); and a runtime error raised from *inside*
a prelude function's own body reports a line number relative to
`Prelude.fs`'s own source text, not the user's file, since nothing in
this pipeline has ever needed multi-file source-position tracking before
now.

23 new xUnit tests (5 parser: the swallowed-argument bug and its fix,
the Grouping-comma-scope fix, both standalone and inside a vector
literal; 6 prelude-specific: the embedded source scans/parses/resolves/
compiles cleanly on its own, a user program can call all four as ordinary
globals once merged, redeclaring any of them is an "already declared"
error) and 10 new Catch2 tests (`push`/`pop`/`length`/`reverse` presence,
happy path, mutation-vs-pure-return semantics, and non-vector-argument
runtime errors for each). `langspec/examples/array_stdlib.iqx` verified
end to end through the real `iqaloxc`+`iqaloxvm` toolchain, exercising
all eight functions plus `sort`'s non-mutating contract.

**Phase 6 — Matrices.** Nested-vector convention plus dedicated stdlib
(multiply, transpose, elementwise ops — §2.5). Mostly a stdlib-layer
phase once indexing exists; no new literal grammar per decision 5.

**Phase 7 — Property and method visibility (`pub`/`mut`).** The biggest
single change to the object model this version — property declarations,
the internal-vs-external access split (decisions 10-11), and needs §2.3
(subclass privacy scope) resolved first. Also the phase that determines
how existing `langspec/examples/classes.iqx`/`inheritance.iqx` are
affected (§2.6) — every external method call in every existing example
needs an explicit `pub`, found and fixed here, not discovered later.

**Phase 8 — Mixins and traits.** `with`-dynamic and `trait`/`use`-static
composition, extending `Inherit`'s existing method-table-copy mechanism.
Needs §2.1 (static conflict resolution) and §2.2 (dynamic linearization
algorithm) resolved first. Builds on Phase 7's (by-then-updated)
class/property model.

**Phase 9 — Conformance and docs.** Resolve §2.6's conformance-suite split
in practice (`scripts/conformance-test.sh` pointed at `langspec/versions/0.1/`,
for the `poc/` comparison; the current, `0.2`-syntax top-level examples
become a `compiler/`+`vm/`-only check);
fork `docs/LANGUAGE.md` into `docs/LANGUAGE-0.1.md` (frozen) plus a new,
current `docs/LANGUAGE.md` for `0.2` — the same fork-not-addendum pattern
`docs/PLAN-0.1.md`'s own Phase 10 used; `ROADMAP.md` marks `0.2` delivered
and moves the active-target goalposts to `0.3`.

## 6. Testing strategy

Same split `0.1` already established (`docs/PLAN-0.1.md` §7): xUnit for
`compiler/`, Catch2 for `vm/`, plus whatever `langspec/examples/`
strategy §2.6 lands on in practice. No new testing *infrastructure*
needed — this version is entirely new language surface on an
already-proven pipeline, not a new implementation to stand up.

## 7. Risks

- **Decisions 8-11 (private-by-default properties *and* methods) are a
  real breaking change to `0.1`'s object model**, not an additive
  feature — every existing class-using script needs auditing for any
  property or method ever accessed from outside its own class, which
  would newly require an explicit `pub`. See §2.6 for the concrete
  conformance-suite fallout, and decision 13 for the `langspec/`
  reorganization this forces.
- **Four new keyword/token additions in one version** (`->`, `<-`, `...`
  finally given meaning, plus the new `pub` keyword) grows the
  scanner/parser's surface area meaningfully in a single pass — keep test
  density up per new token, the same lesson `0.1`'s own scanner phase
  already demonstrated (five real bugs found there specifically because
  the port was written carefully token-by-token, not copied wholesale).
- **Mixin/trait semantics are the least fully specified area of this
  plan** (§2.1, §2.2) — don't start Phase 8 without those resolved, or
  Phase 7's already-changed class model risks getting extended twice in
  slightly different directions.
- **`langspec/` now needs an ongoing versioning convention it never
  needed before** (decision 13, §2.6) — `langspec/versions/<version>/`
  was chosen specifically to avoid colliding with the pre-existing,
  differently-scoped `langspec/archived/<version>/` planning snapshots;
  don't let Phase 0 casually reuse the bare `langspec/<version>/` form
  instead.

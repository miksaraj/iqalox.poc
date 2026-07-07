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
   named explicitly there now rather than just implied.
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
4. **Exact array-manipulation stdlib surface.** "Julia-inspired
   single/multi-dimensional array manipulation" is a vision statement, not
   a function list. Candidates to confirm/prune before Phase 5: `push`,
   `pop`, `map`, `filter`, `reduce`, `sort`, `reverse`, `length` — as
   builtin functions (matching `print`/`concat`'s no-parens-call
   convention) or as methods on vectors (which `0.1`'s object model
   explicitly says primitives don't have, `docs/LANGUAGE.md` §13 item 5)?
   Blocks Phase 5.
5. **Exact matrix stdlib surface.** Multiply, transpose, elementwise
   arithmetic are the obvious Julia-flavored candidates — full list not
   yet confirmed. Blocks Phase 6.
6. **`langspec/`'s versioning convention, and where the "current version
   at the top, old versions in a numbered subdirectory" rule actually
   puts things.** Resolved in principle (PR review on the first draft):
   the current version's grammar docs and examples live at
   `langspec/`'s top level as always; once a version is superseded, its
   snapshot moves into a subdirectory named after that version, and
   `langspec/examples/*.iqx` gets updated (and possibly extended with new
   fixtures) for the new current version. **Not yet resolved: the exact
   subdirectory name.** `langspec/archived/0.1/` (and `/0.2/`, `/0.3/`)
   **already exist** — but per `ROADMAP.md`'s own renumbering note, those
   are pre-renumbering *planning-era* snapshots from before `0.1`/`0.2`
   meant what they mean today, not a snapshot of the real, shipped `0.1`.
   Reusing the bare version number for the real `0.1` snapshot would
   collide with (or worse, get confused for) that unrelated existing
   directory. Needs an explicit name before Phase 9 actually performs the
   move — a distinct top-level location (e.g. `langspec/versions/0.1/`,
   name not decided) is one option, keeping `langspec/archived/` scoped
   to exactly what it already means. Blocks Phase 9.
7. **What happens to the Phase 9 conformance suite once `langspec/
   examples/` moves to `0.2` syntax.** `poc/` is frozen and can't parse
   `0.2`'s new syntax at all — once the *current*, top-level
   `langspec/examples/*.iqx` stops being `0.1-poc`-compatible,
   `scripts/conformance-test.sh` can no longer diff `poc/` output against
   the live top-level examples the way it does today. It would need to
   point specifically at wherever `0.1`'s examples snapshot lands (§2.6)
   for the `poc/`-vs-`compiler/`+`vm/` comparison, while the *current*
   top-level examples become a `compiler/`+`vm/`-only smoke test with no
   `poc/` counterpart at all. Blocks Phase 9.

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
| Vector indexing (`v[i]` get/set) | §1.6 | Not started |
| Lambdas (`(a, b) -> expr`) | §1.1 | Not started |
| Cons operator (`[item \| list]`) | §1.2 | Not started |
| List comprehensions (single generator) | §1.2-4 | Not started |
| Vector-literal spread (`[...a, ...b]`) | §1.7 | Not started |
| Array-manipulation stdlib | §2.4 (list not yet final) | Not started |
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

**Phase 0 — `langspec/` versioning move.** Resolve §2.6's naming question,
then move `0.1`'s current `langspec/` snapshot (grammar docs + examples)
into its own versioned location before anything else changes what's at
the top level — mirrors `docs/PLAN-0.1.md`'s own Phase 0 (repository
reorganization) pattern of doing the structural move first, separately
from any feature work, so it's easy to review on its own.

**Phase 1 — Indexing.** `v[i]` read/write, bounds-checked, 0-based. New
postfix grammar on any primary expression (not just identifiers — `f()[0]`
should work too, matching how `.property` access already composes with
calls).

**Phase 2 — Lambdas.** `(a, b) -> expr`, single-expression body, closing
over enclosing scope exactly like a named nested function already does.

**Phase 3 — Cons and list comprehensions.** `[item | list]`,
`[expr | pattern <- iterable]` (§1.3's single-generator, no-guards slice).

**Phase 4 — Vector-literal spread.** `[...a, ...b]`.

**Phase 5 — Array-manipulation standard library.** Depends on Phases 1-2
(indexing, lambdas) for anything map/filter/reduce-shaped. Needs §2.4
resolved first.

**Phase 6 — Matrices.** Nested-vector convention plus dedicated stdlib
(multiply, transpose, elementwise ops — §2.5). Mostly a stdlib-layer
phase once indexing exists; no new literal grammar per decision 5.

**Phase 7 — Property and method visibility (`pub`/`mut`).** The biggest
single change to the object model this version — property declarations,
the internal-vs-external access split (decisions 10-11), and needs §2.3
(subclass privacy scope) resolved first. Also the phase that determines
how existing `langspec/examples/classes.iqx`/`inheritance.iqx` are
affected (§2.7) — every external method call in every existing example
needs an explicit `pub`, found and fixed here, not discovered later.

**Phase 8 — Mixins and traits.** `with`-dynamic and `trait`/`use`-static
composition, extending `Inherit`'s existing method-table-copy mechanism.
Needs §2.1 (static conflict resolution) and §2.2 (dynamic linearization
algorithm) resolved first. Builds on Phase 7's (by-then-updated)
class/property model.

**Phase 9 — Conformance and docs.** Resolve §2.7's conformance-suite split
in practice (`scripts/conformance-test.sh` pointed at wherever Phase 0
moved `0.1`'s examples to, for the `poc/` comparison; the current,
`0.2`-syntax top-level examples become a `compiler/`+`vm/`-only check);
fork `docs/LANGUAGE.md` into `docs/LANGUAGE-0.1.md` (frozen) plus a new,
current `docs/LANGUAGE.md` for `0.2` — the same fork-not-addendum pattern
`docs/PLAN-0.1.md`'s own Phase 10 used; `ROADMAP.md` marks `0.2` delivered
and moves the active-target goalposts to `0.3`.

## 6. Testing strategy

Same split `0.1` already established (`docs/PLAN-0.1.md` §7): xUnit for
`compiler/`, Catch2 for `vm/`, plus whatever `langspec/examples/`
strategy §2.6-7 lands on in practice. No new testing *infrastructure*
needed — this version is entirely new language surface on an
already-proven pipeline, not a new implementation to stand up.

## 7. Risks

- **Decisions 8-11 (private-by-default properties *and* methods) are a
  real breaking change to `0.1`'s object model**, not an additive
  feature — every existing class-using script needs auditing for any
  property or method ever accessed from outside its own class, which
  would newly require an explicit `pub`. See §2.7 for the concrete
  conformance-suite fallout, and §2.6 for the `langspec/` reorganization
  this forces.
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
  needed before** (§2.6-7), and the obvious naming choice for it
  (`langspec/<version>/`) collides with the pre-existing, differently-
  scoped `langspec/archived/<version>/` planning snapshots — resolve the
  naming question deliberately (§2.6) rather than picking whichever path
  happens to not immediately error when Phase 0 starts.

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
- Getters and setters — resolved this round as *forced* (mandatory
  encapsulation), not optional
- Mixin support (`with`) and trait support (`trait`/`use`) — resolved this
  round as a PHP-style/Scala-style split (§1)

## 1. Design decisions (resolved)

Resolved this planning round, 2026-07-06 — recorded with the reasoning,
not just the answer, per this project's convention:

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
   `0.2`.** `[expr | pattern <- iterable]` only — multiple
   comma-separated generators (nested/Cartesian iteration) and boolean
   guards are explicitly *not* in this pass; the original
   `[x + y | x <- xs, y <- ys, x not y]`-style sketch in `ROADMAP.md` is
   the eventual shape, not the `0.2` one. Logged so nobody mistakes the
   smaller slice for the whole feature.
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
   indices and slice syntax (`v[a:b]`) are explicitly deferred — `0.3`
   already has "array-manipulation standard library improvements" on the
   roadmap as the natural home for them.
7. **Variadic unpacking (`...`) ships as vector-literal spread only:
   `[...a, ...b]`.** Splices `a`'s and `b`'s elements into the new vector.
   Variadic *parameters* (`fun f(...args)`) and call-site spread
   (`f(...someVector)`) are explicitly **out of scope** for `0.2` — variadic
   parameters in particular are in real tension with `0.1`'s stated design
   ("a call's arity is fixed by the callee's declaration, not inferred from
   how many arguments happen to be written"), and resolving that tension
   wasn't asked for or answered this round. If a future version wants
   either, that's a fresh design question, not an extension of this one.
8. **Getters/setters are forced, declared via `get`/`set` blocks on a
   property declaration** (own keyword, tentatively `prop` — see the
   engineering-choice note at the end of this list): `prop x { get { ... }
   set(v) { ... } }` inside a class body.
9. **All instance state must be a declared `prop`.** `0.1`'s "a field
   springs into existence on first assignment" model is gone for `0.2`
   classes — there is no more bare, undeclared `self.x = value` conjuring
   a new field. This is a real breaking change to the object model, not
   an additive one (see §2's conformance-fixture note and §7's risks).
10. **Inside the class's own methods, `self.x` bypasses `get`/`set` and
    reads/writes the backing slot directly; anyone else's `instance.x`
    always goes through the declared accessor.** Concretely, this means a
    `prop`'s own `get`/`set` bodies use `self.x` to mean "the raw backing
    value" (so `prop name { get { return self.name; } }` doesn't recurse
    into its own getter), and existing patterns like `init(name) { self.name
    = name; }` keep working almost unchanged internally — they just now also
    need a `prop name { get { ... } }` declared somewhere in the class body
    for *external* access (`duck.name`) to be legal at all. This is my
    working interpretation of what you picked, not something spelled out
    in so many words — flag it if it's not what you meant, ideally before
    Phase 7 (§5) starts.
11. **Mixins/traits split by *which keyword*, not by *where* they're
    used.** `trait T { ... }` composed via `use` always resolves as a
    Scala-style dynamic linearization chain (exact algorithm still open,
    §2); a separate, plainer `with`-only mixin form (`class C extends Base
    with M1, M2 { }`) always does flat, PHP-style static copying —
    literally copying the mixin's members in at class-declaration time,
    extending the same static method-table-copy mechanism `0.1`'s `extends`
    already uses, no runtime dispatch chain involved. Both forms are usable
    "anywhere" per this round's answer, but see §2 for why `0.2` can only
    actually deliver the class-scoped case (module-level composition needs
    `module` itself, which isn't real until `0.5`).

**One engineering choice made without further sign-off** (per `CLAUDE.md`:
tooling/naming that doesn't change observable language behavior in a way
already decided is fair game, but keyword *spelling* is borderline enough
to flag explicitly rather than bury): the property-declaration keyword is
tentatively **`prop`**. Nothing above hinges on that exact spelling —
`property`, `field`, `attr`, whatever you'd rather have — this is the one
place in this document where I picked a name rather than asked, and I'm
saying so plainly instead of letting it slide by as if it had been
decided.

## 2. Open questions (flagged, not decided)

Per `CLAUDE.md`, these block the phases that depend on them (noted per
item) — not the whole plan. Add to this list rather than silently
resolving, the same convention `docs/PLAN-0.1-POC.md` and
`docs/PLAN-0.1.md` §2 already used.

1. **Mixin conflict resolution.** If two `with`-listed mixins (or a mixin
   and the class's own superclass) define the same member name, what
   happens? PHP itself requires explicit `insteadof`/`as` conflict
   resolution rather than silently picking one — does `0.2` need the same,
   or is last-listed-wins (matching a flat, ordered copy) acceptable?
   Blocks Phase 8.
2. **Trait linearization algorithm.** "Scala-style dynamic" doesn't by
   itself specify the resolution order when a class uses multiple traits,
   or how a trait's `super`-like call to "the next one in the chain" is
   actually resolved (Scala uses C3 linearization over the full
   inheritance graph). Does `0.2` need the full algorithm, or a simpler
   ordered-fallback approximation for a first pass? Blocks Phase 8.
3. **Read-only properties.** A `prop` declared with only `get`, no `set` —
   is external assignment to it a compile-time error (matching
   immutability-by-default's existing spirit) or a runtime error (matching
   how `0.1`'s immutable-`var` reassignment used to work before compile-time
   enforcement landed)? Blocks Phase 7.
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
6. **`langspec/examples/*.iqx` and the Phase 9 conformance suite, once
   props break `0.1`-syntax field access.** `poc/` is frozen and won't
   grow `prop`/indexing/lambdas/etc., so any existing or new fixture using
   `0.2`-only syntax has no `poc/` counterpart to diff against — and
   worse, decision 9 (all state must be a declared `prop`) means
   `classes.iqx`/`inheritance.iqx` as they exist *today* (plain
   `self.name = name`, no `prop` declaration, then external `duck.quack()`
   which only ever accesses fields from inside the class — actually still
   legal under decision 10's self-bypass rule) need to be re-checked
   fixture-by-fixture for whether they ever access a field from *outside*
   its class, which would newly require a `prop` declaration. Proposed
   resolution, flagged rather than silently assumed: keep
   `langspec/examples/*.iqx` exactly as `0.1-poc`-parity fixtures forever
   (still diffed against `poc/`), and add a new
   `langspec/examples-0.2/` (or similar) directory for fixtures exercising
   `0.2`-only syntax, checked only against `compiler/`+`vm/` with no `poc/`
   counterpart. `scripts/conformance-test.sh` would need a matching split.
   Blocks Phase 9 (the docs/conformance pass), not earlier phases.
7. **Module-scoped trait/mixin composition is out of reach until `0.5`.**
   Decision 11 says both forms work "anywhere," but `module` itself has no
   grammar or semantics yet (`ROADMAP.md` slates real module support for
   `0.5`). `0.2` can only actually deliver the class-scoped case
   (`class C extends Base with M1, M2`, `class D { use T; }`); revisit
   module-level composition when `0.5`'s module work starts. Not a blocker
   for `0.2` — noted so it isn't mistaken for a dropped requirement later.

## 3. Grammar and architecture additions (overview)

No new implementation stack this time — everything below extends the
existing `compiler/`+`vm/` pipeline from `docs/PLAN-0.1.md` §3.

**New tokens** (`compiler/src/Token.fs`/`Scanner.fs`): `Arrow` (`->`),
`LeftArrow` (`<-`); `Ellipsis` (`...`, already scanned since `0.1-poc`,
just never given grammar until now); a new `prop` keyword (spelling per
§1's engineering-choice note) plus `get`/`set` as contextual keywords
(only meaningful inside a `prop` block, so they needn't become globally
reserved words the way `with`/`trait`/`use`/`module` already are).

**New AST/Bound node shapes** (`Ast.fs`/`Bound.fs`): an `Index`/`IndexSet`
expression pair (parsed as a postfix `[...]` on any primary, distinct from
a vector *literal*'s leading `[...]`); a `Lambda` expression (parameter
list + single body expression, resolved exactly like a named function's
parameter list/closure capture, just with no name to bind); a `Cons`
expression and a `ListComprehension` expression (both `[...]`-delimited,
told apart per decision 2's lookahead); a `PropDecl` class member shape
alongside the existing `FunctionDecl` (name, `get` body, optional `set`
parameter + body); `with`-mixin and `trait`/`use` composition on
`ClassStmt`.

**`Resolver.fs`**: lambdas resolve like an anonymous `FunctionDecl` (same
scope/slot/upvalue machinery `0.1` already has for nested named
functions); `self.x` vs `instance.x` needs a genuinely new concept this
version — "is this property access happening inside a method of the same
class the property belongs to" — to implement decision 10's bypass rule,
since `0.1`'s `GetProperty`/`SetProperty` never distinguished internal
from external access at all; mixin/trait member resolution happens at
class-declaration time, extending the existing superclass-method-table-copy
mechanism (`vm/src/vm.cpp`'s `Inherit` opcode handler) to also fold in
`with`-listed mixins' members (flat copy) or `use`d traits' members
(linearized chain, pending §2's algorithm question).

**`Codegen.fs`**: new opcodes for indexed get/set (bounds-checked at
runtime, matching the existing `GetProperty`/`SetProperty` error-reporting
style); `Closure` already covers lambdas with no new opcode needed, since
a lambda is just a nameless `FunctionConstant`; cons compiles to
build-a-new-vector-by-prepending (no dedicated opcode either — expands to
existing `BuildVector` plus copying the tail vector's elements); a list
comprehension desugars to a loop that builds a vector via repeated
`BuildVector`-equivalent pushes, closer to how a `for` loop already
compiles than to anything genuinely new; vector-literal spread
(`[...a, ...b]`) is a `BuildVector`-time flattening step.

**`vm/`**: `ObjVector` gains bounds-checked indexed get/set (it already
has a `std::vector<Value> elements` — indexing needs no new heap-object
shape, just new opcode handlers); `ObjInstance`'s `fields` map changes
shape or is replaced outright once props are the only state (exact
representation is Phase 7's design work, not decided here); mixin/trait
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
| Forced getters/setters (`prop` declarations) | §1.8-10, §2.3 | Not started |
| Mixins (`with`, static copy) | §1.11, §2.1 | Not started |
| Traits (`trait`/`use`, dynamic linearization) | §1.11, §2.2 | Not started |

## 5. Suggested sequencing

A proposed order, not a commitment — reorder freely. Indexing first since
almost everything else either needs it directly (matrices, array stdlib)
or benefits from the `[...]`-postfix grammar work being already proven
before cons/comprehensions reuse the same bracket. Props/mixins pushed
later since they're the largest, most self-contained (class-system-only)
changes and don't block the array/stdlib work at all — feel free to swap
their order if you'd rather tackle the biggest risk first instead of last.

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

**Phase 7 — Forced getters/setters.** The biggest single change to the
object model this version — `prop` declarations, the self-vs-external
access split (decision 10), and needs §2.3 (read-only enforcement)
resolved first. Also the phase that determines how existing
`langspec/examples/classes.iqx`/`inheritance.iqx` are affected (§2.6).

**Phase 8 — Mixins and traits.** `with`-static and `trait`/`use`-dynamic
composition, extending `Inherit`'s existing method-table-copy mechanism.
Needs §2.1 (conflict resolution) and §2.2 (linearization algorithm)
resolved first. Builds on Phase 7's (by-then-updated) class/property model.

**Phase 9 — Conformance and docs.** Resolve §2.6's fixture-split question
in practice (new `langspec/examples-0.2/`-equivalent fixtures, an updated
`scripts/conformance-test.sh`); fork `docs/LANGUAGE.md` into
`docs/LANGUAGE-0.1.md` (frozen) plus a new, current `docs/LANGUAGE.md` for
`0.2` — the same fork-not-addendum pattern `docs/PLAN-0.1.md`'s own Phase
10 used; `ROADMAP.md` marks `0.2` delivered and moves the active-target
goalposts to `0.3`.

## 6. Testing strategy

Same split `0.1` already established (`docs/PLAN-0.1.md` §7): xUnit for
`compiler/`, Catch2 for `vm/`, plus whatever `langspec/examples/` fixture
strategy §2.6 lands on. No new testing *infrastructure* needed — this
version is entirely new language surface on an already-proven pipeline,
not a new implementation to stand up.

## 7. Risks

- **Decision 9 (forced props, no more bare fields) is a real breaking
  change to `0.1`'s object model**, not an additive feature — every
  existing class-using script needs auditing for whether it ever accesses
  a field from outside its own class (which would newly require a `prop`
  declaration). See §2.6 for the concrete fixture-strategy fallout.
- **Three new operator tokens in one version** (`->`, `<-`, `...`
  finally given meaning) grows the scanner/parser's surface area
  meaningfully in a single pass — keep test density up per new token,
  the same lesson `0.1`'s own scanner phase already demonstrated (five
  real bugs found there specifically because the port was written
  carefully token-by-token, not copied wholesale).
- **Mixin/trait semantics are the least fully specified area of this
  plan** (§2.1, §2.2) — don't start Phase 8 without those resolved, or
  Phase 7's already-changed class model risks getting extended twice in
  slightly different directions.
- **`langspec/examples/` now needs a versioning story it never needed
  before** (§2.6) — `0.1`'s examples were the *only* examples, always
  checked against `poc/`. `0.2` is the first version where "does this
  script even mean the same thing in every implementation" stops being
  true by construction, since `poc/` is frozen and `0.2` syntax has no
  `poc/` equivalent at all.

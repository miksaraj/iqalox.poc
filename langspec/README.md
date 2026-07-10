# `langspec/` — the Iqalox language specification

This directory holds the **language specification**: the syntax grammar, a directory
README (this file) explaining how to navigate the spec itself, and runnable example
programs — kept independent of any one implementation (`poc/`, `compiler/`+`vm/`). See
the root `README.md` for the lexical grammar and precedence table, `../ROADMAP.md` for
the full version plan, and `../CLAUDE.md` for the repo-wide conventions this directory
follows.

## Directory layout

```
langspec/
├── README.md            this file
├── SYNTAX_GRAMMAR.md     current active-target version's grammar
├── examples/             current active-target version's example programs
├── versions/             frozen snapshots of versions that have fully shipped
│   ├── 0.1/
│   │   ├── README.md
│   │   ├── SYNTAX_GRAMMAR.md
│   │   └── examples/
│   └── 0.2/
│       ├── README.md
│       ├── SYNTAX_GRAMMAR.md
│       └── examples/
└── archived/             unrelated: pre-renumbering planning-era snapshots (see below)
    ├── 0.1/
    ├── 0.2/
    └── 0.3/
```

- **Top level** (`README.md`, `SYNTAX_GRAMMAR.md`, `examples/`) is the **current active
  target** — right now, `0.3` (`../docs/PLAN-0.3.md`). "Active target" means this is
  what the language is being designed and built *towards*, not necessarily what's fully
  implemented yet: `compiler/`+`vm/` land `0.3`'s features incrementally across several
  phases, and `../docs/PLAN-0.3.md` §4's feature checklist tracks exactly what's landed
  so far. Until every phase lands, the top-level `examples/` describe the target spec
  and may not yet run against `compiler/`+`vm/` — see `../CLAUDE.md`'s "Example scripts"
  convention for how this is tracked during the transition.
- **`versions/<version>/`** holds a frozen, complete snapshot of a version's spec once
  it has fully shipped and the next version's work begins moving the top level forward.
  Each snapshot is a full copy of what the top level looked like at that version's
  release — its own `README.md`, `SYNTAX_GRAMMAR.md`, and `examples/`. `versions/0.1/`
  was moved here when `0.2` planning began (`../docs/PLAN-0.2.md` decision 13, Phase 0);
  `versions/0.2/` was moved here the same way when `0.3` planning began
  (`../docs/PLAN-0.3.md`, Phase 0).
- **`archived/<version>/`** is a **different, unrelated thing** — don't confuse the two.
  These are frozen snapshots from *before* `../ROADMAP.md`'s version renumbering
  decision: early planning drafts of what "`0.1`," "`0.2`," "`0.3`" meant back when that
  document was first being written, which do **not** correspond to the real, shipped
  versions of the same name today (see `../ROADMAP.md`'s "Old → new version mapping"
  table for the full translation). They're kept purely as a historical record of how
  the plan evolved, and are never edited or added to. This naming collision with
  `versions/` is exactly why `versions/` was named the way it was, rather than reusing
  a bare version number at the top of `langspec/` itself.
- **`examples/`** (wherever it appears — top level or inside a `versions/<n>/`
  snapshot) holds cross-implementation conformance fixtures: the same `.iqx` source,
  expected to produce the same output regardless of which implementation runs it (where
  more than one implementation can run it at all — see `../CLAUDE.md`).

## Finding what you're looking for

- **"What does Iqalox actually do today, as shipped?"** → `../docs/LANGUAGE.md` (the
  full prose reference for `0.2`, the current primary implementation) — or, for the
  terser grammar-level equivalent, `versions/0.2/SYNTAX_GRAMMAR.md`/
  `versions/0.2/examples/`. For `0.1` specifically, see `../docs/LANGUAGE-0.1.md` and
  `versions/0.1/SYNTAX_GRAMMAR.md`/`versions/0.1/examples/`. The top-level
  `SYNTAX_GRAMMAR.md`/`examples/` describe `0.3`, the *target*, not what's shipped yet.
- **"What's being planned/built for the next version?"** → `../docs/PLAN-0.3.md` for
  the design decisions, open questions, and implementation status behind `0.3`, the
  active target — this directory's own top-level `SYNTAX_GRAMMAR.md`/`examples/` track
  it as phases land.
- **"What did an early planning draft look like, before versions got renumbered?"** →
  `archived/<version>/` — but read `../ROADMAP.md`'s renumbering note first so the
  version number there isn't mistaken for today's version of the same name.

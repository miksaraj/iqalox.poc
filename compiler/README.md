# iqaloxc

The `0.1` compiler frontend, in F#. Emits the bytecode format `vm/` loads
and executes (currently format v0 -- see `src/Bytecode.fs` for the exact
layout, mirrored from `vm/src/bytecode.hpp`). See `docs/PLAN-0.1.md` for
the full plan.

Phase 1 scope only: `iqaloxc` currently emits a fixed, hardcoded chunk
rather than actually compiling a `.iqx` source file -- the scanner/parser/
codegen come in Phases 2-5.

## Build

```
dotnet build
```

## Test

```
dotnet test
```

## Run

```
dotnet run --project src/Iqaloxc.fsproj -- <output bytecode file>
```

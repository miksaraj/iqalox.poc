# iqaloxvm

The `0.1` bytecode VM backend, in C++23. Loads and executes the bytecode
format `compiler/` emits (format v1 -- see `src/bytecode.hpp` for the exact
layout, mirroring `compiler/src/Bytecode.fs`'s own doc comment). See
`docs/PLAN-0.1.md` for the full plan.

A stack-based interpreter (`src/vm.hpp`/`.cpp`) over a tagged-union `Value`
(`src/value.hpp`/`.cpp`) and a small heap-object hierarchy -- strings,
vectors, functions, closures, upvalues, native functions
(`src/object.hpp`) -- collected by a mark-sweep tracing garbage collector
built into `Vm` itself. Covers every `0.1-poc`-equivalent expression/
statement, functions, and closures, plus a native standard library
(`src/natives.hpp`/`.cpp`: `print`, `concat`) defined as globals before any
program runs. Classes/`self`/`super` are recognized but not yet executable
(Phase 8), so a program that declares one still ends in a clean runtime
error rather than running to completion.

## Build

```
cmake -S . -B build -DCMAKE_BUILD_TYPE=Debug
cmake --build build -j
```

## Test

```
cd build && ctest --output-on-failure
```

## Run

```
build/iqaloxvm <bytecode file>
```

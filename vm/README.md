# iqaloxvm

The `0.1` bytecode VM backend, in C++23. Loads and executes the bytecode
format `compiler/` emits (currently format v0 -- see `src/bytecode.hpp` for
the exact layout). See `docs/PLAN-0.1.md` for the full plan.

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

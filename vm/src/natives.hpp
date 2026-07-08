#pragma once

#include <vector>

#include "value.hpp"

namespace iqalox {

class Vm;

// print(value) -- writes `stringify(value)` followed by a newline to
// stdout; returns `nil`. Arity 1, matching `poc/src/interpreter.py`'s
// `_native_print`.
Value nativePrint(Vm& vm, const std::vector<Value>& args);

// concat(vector) -- joins `stringify` of each element with no separator,
// returning a new string. Arity 1, matching poc's `_native_concat` --
// except a non-vector argument is a clean `RuntimeError` here rather than
// the uncaught Python `TypeError` poc itself raises (see
// docs/PLAN-0.1-POC.md's running list).
Value nativeConcat(Vm& vm, const std::vector<Value>& args);

// push(vector, value) -- appends `value` to `vector`'s own element list in
// place (docs/PLAN-0.2.md Phase 5) and returns `nil`, matching `print`'s
// "side-effecting builtin returns nil" convention. Arity 2.
Value nativePush(Vm& vm, const std::vector<Value>& args);

// pop(vector) -- removes and returns `vector`'s last element in place. A
// runtime error if the vector is empty (nothing to pop), matching every
// other domain-error's error style. Arity 1.
Value nativePop(Vm& vm, const std::vector<Value>& args);

// length(vector) -- element count. The same check-and-count logic as the
// internal-only `VectorLength` opcode (docs/PLAN-0.2.md Phase 3), just
// exposed here as this phase's first user-facing way to ask a vector its
// own length. Arity 1.
Value nativeLength(Vm& vm, const std::vector<Value>& args);

// reverse(vector) -- a *new* vector with `vector`'s elements in reverse
// order; the argument itself is untouched (docs/PLAN-0.2.md Phase 5:
// push/pop mutate in place since that's the whole point of a stack
// operation, but reverse reads as a pure transformation). Arity 1.
Value nativeReverse(Vm& vm, const std::vector<Value>& args);

// docs/PLAN-0.2.md Phase 6: matrices are plain nested vectors (decision
// 5) with no dedicated runtime type, so these four validate their own
// shape and raise a clean, dimension-naming RuntimeError on a mismatch
// (the repository owner's explicit choice) -- none of the four call back
// into user code, so (unlike map/filter/reduce/sort/elementwise) they
// can be true natives with no VM-reentrancy concerns at all. All four
// return a *new* matrix; none mutate their arguments.

// transpose(matrix) -- swap rows and columns. Arity 1.
Value nativeTranspose(Vm& vm, const std::vector<Value>& args);

// multiply(a, b) -- the true matrix product (row-by-column dot
// products), not elementwise multiplication -- a's column count must
// equal b's row count. Arity 2.
Value nativeMultiply(Vm& vm, const std::vector<Value>& args);

// add(a, b) -- elementwise sum; a and b must be the same shape. Arity 2.
Value nativeAdd(Vm& vm, const std::vector<Value>& args);

// subtract(a, b) -- elementwise a - b; a and b must be the same shape.
// Arity 2.
Value nativeSubtract(Vm& vm, const std::vector<Value>& args);

}  // namespace iqalox

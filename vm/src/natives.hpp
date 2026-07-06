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

}  // namespace iqalox

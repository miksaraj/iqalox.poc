#include "value.hpp"

#include "object.hpp"

namespace iqalox {

bool isTruthy(const Value& v) {
    if (isNil(v) || isUndef(v)) return false;
    if (isBool(v)) return asBool(v);
    return true;
}

namespace {

bool objectsEqual(Obj* a, Obj* b) {
    if (a == b) return true;
    if (a->type != b->type) return false;

    switch (a->type) {
        case ObjType::String:
            return static_cast<ObjString*>(a)->value == static_cast<ObjString*>(b)->value;
        case ObjType::Vector: {
            auto& left = static_cast<ObjVector*>(a)->elements;
            auto& right = static_cast<ObjVector*>(b)->elements;
            if (left.size() != right.size()) return false;
            for (size_t i = 0; i < left.size(); ++i) {
                if (!valuesEqual(left[i], right[i])) return false;
            }
            return true;
        }
        // Functions/closures/upvalues have no `poc` equivalent with
        // structural equality -- Python's default `==` (what `is_equal`
        // relies on for everything it doesn't special-case) falls back to
        // identity, already handled by the `a == b` check above.
        case ObjType::Function:
        case ObjType::Closure:
        case ObjType::Upvalue:
            return false;
    }
    return false;
}

}  // namespace

bool valuesEqual(const Value& a, const Value& b) {
    if (a.index() != b.index()) return false;
    if (isNil(a) || isUndef(a)) return true;
    if (isBool(a)) return asBool(a) == asBool(b);
    if (isNumber(a)) return asNumber(a) == asNumber(b);
    return objectsEqual(asObj(a), asObj(b));
}

std::string typeName(const Value& v) {
    if (isNil(v)) return "nil";
    if (isUndef(v)) return "undef";
    if (isBool(v)) return "bool";
    if (isNumber(v)) return "number";
    switch (asObj(v)->type) {
        case ObjType::String: return "string";
        case ObjType::Vector: return "vector";
        case ObjType::Function: return "function";
        case ObjType::Closure: return "function";
        case ObjType::Upvalue: return "upvalue";
    }
    return "value";
}

}  // namespace iqalox

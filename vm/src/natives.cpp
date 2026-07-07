#include "natives.hpp"

#include <iostream>
#include <utility>

#include "object.hpp"
#include "vm.hpp"

namespace iqalox {

Value nativePrint(Vm&, const std::vector<Value>& args) {
    std::cout << stringify(args[0]) << "\n";
    return NilValue;
}

Value nativeConcat(Vm& vm, const std::vector<Value>& args) {
    if (!isObj(args[0]) || asObj(args[0])->type != ObjType::Vector) {
        throw RuntimeError("Argument to 'concat' must be a vector, got " + typeName(args[0]) + ".");
    }

    std::string joined;
    for (const Value& element : static_cast<ObjVector*>(asObj(args[0]))->elements) {
        joined += stringify(element);
    }
    return objValue(vm.allocate<ObjString>(std::move(joined)));
}

Value nativePush(Vm&, const std::vector<Value>& args) {
    if (!isObj(args[0]) || asObj(args[0])->type != ObjType::Vector) {
        throw RuntimeError("Only vectors can be pushed onto, got " + typeName(args[0]) + ".");
    }
    static_cast<ObjVector*>(asObj(args[0]))->elements.push_back(args[1]);
    return NilValue;
}

Value nativePop(Vm&, const std::vector<Value>& args) {
    if (!isObj(args[0]) || asObj(args[0])->type != ObjType::Vector) {
        throw RuntimeError("Only vectors can be popped from, got " + typeName(args[0]) + ".");
    }
    auto& elements = static_cast<ObjVector*>(asObj(args[0]))->elements;
    if (elements.empty()) {
        throw RuntimeError("Cannot pop from an empty vector.");
    }
    Value last = elements.back();
    elements.pop_back();
    return last;
}

Value nativeLength(Vm&, const std::vector<Value>& args) {
    if (!isObj(args[0]) || asObj(args[0])->type != ObjType::Vector) {
        throw RuntimeError("Only vectors have a length, got " + typeName(args[0]) + ".");
    }
    return numberValue(static_cast<double>(static_cast<ObjVector*>(asObj(args[0]))->elements.size()));
}

Value nativeReverse(Vm& vm, const std::vector<Value>& args) {
    if (!isObj(args[0]) || asObj(args[0])->type != ObjType::Vector) {
        throw RuntimeError("Only vectors can be reversed, got " + typeName(args[0]) + ".");
    }
    auto* reversed = vm.allocate<ObjVector>();
    const auto& elements = static_cast<ObjVector*>(asObj(args[0]))->elements;
    reversed->elements.assign(elements.rbegin(), elements.rend());
    return objValue(reversed);
}

}  // namespace iqalox

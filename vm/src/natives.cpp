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

}  // namespace iqalox

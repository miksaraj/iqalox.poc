#include "vm.hpp"

#include <cmath>

#include "bytecode.hpp"

namespace iqalox {

namespace {

constexpr size_t kMaxFrames = 1024;
constexpr size_t kGcHeapGrowFactor = 2;

// Python's `%`/`**` (poc/src/interpreter.py's PERCENT/POWER handling,
// which `0.1` matches rather than diverging from) floor toward the
// divisor's sign, unlike C++'s `std::fmod`, which floors toward the
// dividend's -- e.g. `-1 % 4` is `3` in Python/poc, but `std::fmod(-1, 4)`
// is `-1`. Adjust by one divisor's worth when the signs disagree.
double pythonStyleModulo(double a, double b) {
    double result = std::fmod(a, b);
    if (result != 0.0 && ((result < 0.0) != (b < 0.0))) {
        result += b;
    }
    return result;
}

}  // namespace

Vm::~Vm() {
    Obj* obj = objects;
    while (obj != nullptr) {
        Obj* next = obj->next;
        delete obj;
        obj = next;
    }
}

Value Vm::pop() {
    Value value = stack.back();
    truncateStack(stack.size() - 1);
    return value;
}

const Value& Vm::peek(int distanceFromTop) const { return stack[stack.size() - 1 - static_cast<size_t>(distanceFromTop)]; }

void Vm::truncateStack(size_t newSize) {
    while (openUpvalues != nullptr && openUpvalues->stackIndex >= newSize) {
        ObjUpvalue* upvalue = openUpvalues;
        upvalue->closed = stack[upvalue->stackIndex];
        upvalue->open = false;
        openUpvalues = upvalue->nextOpen;
    }
    stack.resize(newSize);
}

void Vm::writeUpvalue(ObjUpvalue* upvalue, const Value& value) {
    if (upvalue->open) {
        stack[upvalue->stackIndex] = value;
    } else {
        upvalue->closed = value;
    }
}

void Vm::runtimeError(const std::string& message) { throw RuntimeError(message); }

void Vm::checkNotUndef(const Value& value, const std::string& globalName) {
    if (!isUndef(value)) return;
    if (globalName.empty()) {
        runtimeError("Variable accessed before being assigned a value.");
    } else {
        runtimeError("Variable '" + globalName + "' accessed before being assigned a value.");
    }
}

void Vm::checkNumberOperand(const Value& value, const char* message) {
    if (!isNumber(value)) throw RuntimeError(message);
}

void Vm::checkNumberOperands(const Value& a, const Value& b, const char* message) {
    if (!isNumber(a) || !isNumber(b)) throw RuntimeError(message);
}

uint8_t Vm::readByte(CallFrame& frame) { return frame.closure->function->chunk.code[frame.ip++]; }

uint16_t Vm::readU16(CallFrame& frame) {
    const auto& code = frame.closure->function->chunk.code;
    uint16_t value = static_cast<uint16_t>(code[frame.ip]) | (static_cast<uint16_t>(code[frame.ip + 1]) << 8);
    frame.ip += 2;
    return value;
}

Value& Vm::constantAt(CallFrame& frame, uint16_t index) { return frame.closure->function->chunk.constants[index]; }

ObjString* Vm::stringConstantAt(CallFrame& frame, uint16_t index) {
    return static_cast<ObjString*>(asObj(constantAt(frame, index)));
}

void Vm::callValue(const Value& callee, int argCount) {
    if (isObj(callee) && asObj(callee)->type == ObjType::Closure) {
        call(static_cast<ObjClosure*>(asObj(callee)), argCount);
        return;
    }
    runtimeError(typeName(callee) + " value is not callable.");
}

void Vm::call(ObjClosure* closure, int argCount) {
    if (argCount != closure->function->arity) {
        runtimeError("Expected " + std::to_string(closure->function->arity) + " argument(s) but got " +
                     std::to_string(argCount) + ".");
    }
    if (frames.size() >= kMaxFrames) {
        runtimeError("Stack overflow.");
    }

    CallFrame frame;
    frame.closure = closure;
    frame.ip = 0;
    frame.stackBase = stack.size() - static_cast<size_t>(argCount);
    frames.push_back(frame);
}

ObjUpvalue* Vm::captureUpvalue(size_t stackIndex) {
    ObjUpvalue* prev = nullptr;
    ObjUpvalue* current = openUpvalues;
    while (current != nullptr && current->stackIndex > stackIndex) {
        prev = current;
        current = current->nextOpen;
    }
    if (current != nullptr && current->stackIndex == stackIndex) {
        return current;
    }

    ObjUpvalue* created = allocate<ObjUpvalue>(stackIndex);
    created->nextOpen = current;
    if (prev == nullptr) {
        openUpvalues = created;
    } else {
        prev->nextOpen = created;
    }
    return created;
}

void Vm::interpret(ObjFunction* script) {
    // This allocation itself must run before `gcEnabled` flips on --
    // `script` isn't reachable from any root yet, only from this local
    // variable (see `allocate`'s doc comment).
    auto* closure = allocate<ObjClosure>(script);
    push(objValue(closure));
    gcEnabled = true;
    call(closure, 0);
    run();
}

void Vm::run() {
    using bytecode::OpCode;

    for (;;) {
        CallFrame& frame = frames.back();
        // Every chunk `compiler/` emits ends in a `Return` (see
        // `Codegen.fs`'s unconditional trailing `Nil; Return`), which
        // always exits this loop or pops back to a caller before `ip`
        // could run off the end -- this check only exists to fail
        // cleanly on a truncated/malformed *bytecode file* that lacks
        // one, rather than reading past the end of `code`.
        if (frame.ip >= frame.closure->function->chunk.code.size()) {
            runtimeError("Malformed chunk: ran off the end without a RETURN.");
        }
        auto op = static_cast<OpCode>(readByte(frame));

        switch (op) {
            case OpCode::Constant:
                push(constantAt(frame, readU16(frame)));
                break;
            case OpCode::Nil:
                push(NilValue);
                break;
            case OpCode::True:
                push(boolValue(true));
                break;
            case OpCode::False:
                push(boolValue(false));
                break;
            case OpCode::Undef:
                push(UndefValue);
                break;
            case OpCode::Pop:
                pop();
                break;
            case OpCode::PopN:
                truncateStack(stack.size() - readU16(frame));
                break;
            case OpCode::GetLocal: {
                Value value = stack[frame.stackBase + readU16(frame)];
                checkNotUndef(value, "");
                push(value);
                break;
            }
            case OpCode::SetLocal:
                stack[frame.stackBase + readU16(frame)] = peek(0);
                break;
            case OpCode::GetUpvalue: {
                Value value = readUpvalue(frame.closure->upvalues[readU16(frame)]);
                checkNotUndef(value, "");
                push(value);
                break;
            }
            case OpCode::SetUpvalue:
                writeUpvalue(frame.closure->upvalues[readU16(frame)], peek(0));
                break;
            case OpCode::GetGlobal: {
                ObjString* name = stringConstantAt(frame, readU16(frame));
                auto it = globals.find(name->value);
                if (it == globals.end()) {
                    runtimeError("Undefined variable '" + name->value + "'.");
                }
                checkNotUndef(it->second, name->value);
                push(it->second);
                break;
            }
            case OpCode::SetGlobal: {
                ObjString* name = stringConstantAt(frame, readU16(frame));
                auto it = globals.find(name->value);
                if (it == globals.end()) {
                    runtimeError("Undefined variable '" + name->value + "'.");
                }
                it->second = peek(0);
                break;
            }
            case OpCode::DefineGlobal: {
                ObjString* name = stringConstantAt(frame, readU16(frame));
                globals[name->value] = pop();
                break;
            }
            case OpCode::Add: {
                Value b = pop(), a = pop();
                checkNumberOperands(a, b, "Operands must be numbers.");
                push(numberValue(asNumber(a) + asNumber(b)));
                break;
            }
            case OpCode::Subtract: {
                Value b = pop(), a = pop();
                checkNumberOperands(a, b, "Operands must be numbers.");
                push(numberValue(asNumber(a) - asNumber(b)));
                break;
            }
            case OpCode::Multiply: {
                Value b = pop(), a = pop();
                checkNumberOperands(a, b, "Operands must be numbers.");
                push(numberValue(asNumber(a) * asNumber(b)));
                break;
            }
            case OpCode::Divide: {
                Value b = pop(), a = pop();
                checkNumberOperands(a, b, "Operands must be numbers.");
                if (asNumber(b) == 0.0) {
                    runtimeError("Division by zero.");
                }
                push(numberValue(asNumber(a) / asNumber(b)));
                break;
            }
            case OpCode::Modulo: {
                Value b = pop(), a = pop();
                checkNumberOperands(a, b, "Operands must be numbers.");
                push(numberValue(pythonStyleModulo(asNumber(a), asNumber(b))));
                break;
            }
            case OpCode::Power: {
                Value b = pop(), a = pop();
                checkNumberOperands(a, b, "Operands must be numbers.");
                push(numberValue(std::pow(asNumber(a), asNumber(b))));
                break;
            }
            case OpCode::Negate: {
                Value a = pop();
                checkNumberOperand(a, "Operand must be a number.");
                push(numberValue(-asNumber(a)));
                break;
            }
            case OpCode::Not:
                push(boolValue(!isTruthy(pop())));
                break;
            case OpCode::Equal: {
                Value b = pop(), a = pop();
                push(boolValue(valuesEqual(a, b)));
                break;
            }
            case OpCode::NotEqual: {
                Value b = pop(), a = pop();
                push(boolValue(!valuesEqual(a, b)));
                break;
            }
            case OpCode::Greater: {
                Value b = pop(), a = pop();
                checkNumberOperands(a, b, "Operands must be numbers.");
                push(boolValue(asNumber(a) > asNumber(b)));
                break;
            }
            case OpCode::GreaterEqual: {
                Value b = pop(), a = pop();
                checkNumberOperands(a, b, "Operands must be numbers.");
                push(boolValue(asNumber(a) >= asNumber(b)));
                break;
            }
            case OpCode::Less: {
                Value b = pop(), a = pop();
                checkNumberOperands(a, b, "Operands must be numbers.");
                push(boolValue(asNumber(a) < asNumber(b)));
                break;
            }
            case OpCode::LessEqual: {
                Value b = pop(), a = pop();
                checkNumberOperands(a, b, "Operands must be numbers.");
                push(boolValue(asNumber(a) <= asNumber(b)));
                break;
            }
            case OpCode::Jump:
                frame.ip = readU16(frame);
                break;
            case OpCode::JumpIfFalse: {
                uint16_t target = readU16(frame);
                if (!isTruthy(peek(0))) frame.ip = target;
                break;
            }
            case OpCode::JumpIfNotNil: {
                uint16_t target = readU16(frame);
                if (!isNil(peek(0))) frame.ip = target;
                break;
            }
            case OpCode::BuildVector: {
                uint16_t count = readU16(frame);
                auto* vector = allocate<ObjVector>();
                vector->elements.assign(stack.end() - count, stack.end());
                truncateStack(stack.size() - count);
                push(objValue(vector));
                break;
            }
            case OpCode::Call: {
                uint16_t argCount = readU16(frame);
                callValue(peek(argCount), argCount);
                break;
            }
            case OpCode::Closure: {
                uint16_t functionIndex = readU16(frame);
                uint16_t upvalueCount = readU16(frame);
                auto* function = static_cast<ObjFunction*>(asObj(constantAt(frame, functionIndex)));

                std::vector<ObjUpvalue*> upvalues;
                upvalues.reserve(upvalueCount);
                for (uint16_t i = 0; i < upvalueCount; ++i) {
                    bool fromEnclosingLocal = readByte(frame) != 0;
                    uint16_t index = readU16(frame);
                    upvalues.push_back(fromEnclosingLocal ? captureUpvalue(frame.stackBase + index)
                                                           : frame.closure->upvalues[index]);
                }

                auto* closure = allocate<ObjClosure>(function);
                closure->upvalues = std::move(upvalues);
                push(objValue(closure));
                break;
            }
            case OpCode::Return: {
                Value result = pop();
                size_t calleeIndex = frame.stackBase - 1;
                truncateStack(calleeIndex);
                frames.pop_back();
                if (frames.empty()) {
                    return;
                }
                push(result);
                break;
            }
            case OpCode::Class:
            case OpCode::Method:
            case OpCode::Inherit:
            case OpCode::GetProperty:
            case OpCode::SetProperty:
            case OpCode::GetSuper:
                runtimeError("Classes are not yet supported by iqaloxvm (docs/PLAN-0.1.md Phase 8).");
        }
    }
}

// --- Garbage collection (docs/PLAN-0.1.md decision 7: mark-sweep) ---

void Vm::markValue(const Value& value) {
    if (isObj(value)) markObject(asObj(value));
}

void Vm::markObject(Obj* obj) {
    if (obj == nullptr || obj->marked) return;
    obj->marked = true;
    grayStack.push_back(obj);
}

void Vm::markRoots() {
    for (const Value& value : stack) markValue(value);
    for (auto& [name, value] : globals) markValue(value);
    for (ObjUpvalue* upvalue = openUpvalues; upvalue != nullptr; upvalue = upvalue->nextOpen) markObject(upvalue);
    // Every `frames[i].closure` is also sitting on `stack` at its own call
    // site (see `call`'s calling convention note in vm.hpp), so the stack
    // scan above already keeps every active frame's closure reachable --
    // no separate frame-walk needed.
}

void Vm::blackenObject(Obj* obj) {
    switch (obj->type) {
        case ObjType::String:
            break;
        case ObjType::Vector:
            for (const Value& element : static_cast<ObjVector*>(obj)->elements) markValue(element);
            break;
        case ObjType::Function:
            for (const Value& constant : static_cast<ObjFunction*>(obj)->chunk.constants) markValue(constant);
            break;
        case ObjType::Closure: {
            auto* closure = static_cast<ObjClosure*>(obj);
            markObject(closure->function);
            for (ObjUpvalue* upvalue : closure->upvalues) markObject(upvalue);
            break;
        }
        case ObjType::Upvalue:
            markValue(static_cast<ObjUpvalue*>(obj)->closed);
            break;
    }
}

void Vm::traceReferences() {
    while (!grayStack.empty()) {
        Obj* obj = grayStack.back();
        grayStack.pop_back();
        blackenObject(obj);
    }
}

void Vm::sweep() {
    Obj* previous = nullptr;
    Obj* obj = objects;
    while (obj != nullptr) {
        if (obj->marked) {
            obj->marked = false;
            previous = obj;
            obj = obj->next;
        } else {
            Obj* unreached = obj;
            obj = obj->next;
            if (previous == nullptr) {
                objects = obj;
            } else {
                previous->next = obj;
            }
            delete unreached;
        }
    }
}

void Vm::collectGarbage() {
    markRoots();
    traceReferences();
    sweep();
    nextGc = bytesAllocated * kGcHeapGrowFactor;
}

}  // namespace iqalox

#include "vm.hpp"

#include <cmath>

#include "bytecode.hpp"
#include "natives.hpp"

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

Vm::Vm() { defineNatives(); }

void Vm::defineNatives() {
    globals["print"] = objValue(allocate<ObjNativeFunction>("print", 1, nativePrint));
    globals["concat"] = objValue(allocate<ObjNativeFunction>("concat", 1, nativeConcat));
    // docs/PLAN-0.2.md Phase 5: push/pop/length/reverse need direct access
    // to an ObjVector's own element list, so they're true natives; map/
    // filter/reduce/sort don't (see compiler/src/Prelude.fs) -- they're
    // ordinary Iqalox functions the compiler prepends to every program.
    globals["push"] = objValue(allocate<ObjNativeFunction>("push", 2, nativePush));
    globals["pop"] = objValue(allocate<ObjNativeFunction>("pop", 1, nativePop));
    globals["length"] = objValue(allocate<ObjNativeFunction>("length", 1, nativeLength));
    globals["reverse"] = objValue(allocate<ObjNativeFunction>("reverse", 1, nativeReverse));
}

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

void Vm::runtimeError(const std::string& message) {
    // `frames` is only ever empty here for the top-level script's own
    // `call()` inside `interpret()`, before `run()`'s loop -- and that
    // call's arity always matches (0 == 0), so it can never actually
    // throw. Guarded anyway rather than assumed.
    if (frames.empty()) {
        throw RuntimeError(message);
    }
    const CallFrame& frame = frames.back();
    const auto& lines = frame.closure->function->chunk.lines;
    if (frame.currentInstructionIp < lines.size()) {
        throw RuntimeError(message + "\n[line " + std::to_string(lines[frame.currentInstructionIp]) + "]");
    }
    throw RuntimeError(message);
}

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

size_t Vm::checkVectorIndex(const Value& receiver, const Value& indexValue) {
    if (!isObj(receiver) || asObj(receiver)->type != ObjType::Vector) {
        runtimeError("Only vectors can be indexed, got " + typeName(receiver) + ".");
    }
    if (!isNumber(indexValue)) {
        runtimeError("Vector index must be a number, got " + typeName(indexValue) + ".");
    }
    double indexNum = asNumber(indexValue);
    if (indexNum < 0.0 || std::floor(indexNum) != indexNum) {
        runtimeError("Vector index must be a non-negative integer, got " + stringify(indexValue) + ".");
    }
    auto* vector = static_cast<ObjVector*>(asObj(receiver));
    size_t index = static_cast<size_t>(indexNum);
    if (index >= vector->elements.size()) {
        runtimeError("Vector index " + std::to_string(index) + " out of range for vector of length " +
                      std::to_string(vector->elements.size()) + ".");
    }
    return index;
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
    if (isObj(callee)) {
        Obj* obj = asObj(callee);
        switch (obj->type) {
            case ObjType::Closure:
                call(static_cast<ObjClosure*>(obj), argCount);
                return;
            case ObjType::NativeFunction:
                callNative(static_cast<ObjNativeFunction*>(obj), argCount);
                return;
            case ObjType::Class:
                callClass(static_cast<ObjClass*>(obj), argCount);
                return;
            case ObjType::BoundMethod: {
                auto* bound = static_cast<ObjBoundMethod*>(obj);
                size_t calleeIndex = stack.size() - static_cast<size_t>(argCount) - 1;
                stack[calleeIndex] = bound->receiver;
                callMethod(bound->method, argCount, /*isInitializer=*/false);
                return;
            }
            default:
                break;
        }
    }
    runtimeError(typeName(callee) + " value is not callable.");
}

void Vm::callNative(ObjNativeFunction* native, int argCount) {
    if (argCount != native->arity) {
        runtimeError("Expected " + std::to_string(native->arity) + " argument(s) but got " +
                     std::to_string(argCount) + ".");
    }

    // Copied into a plain `std::vector` for the native function's own
    // simple ABI (`deque` doesn't offer a contiguous view to hand out
    // instead). Safe even though `native->function` may itself allocate
    // (`concat` builds its result string): the arguments stay right where
    // they are on `stack` -- still scanned as a GC root -- until
    // `truncateStack` removes them *after* the call returns.
    std::vector<Value> args(stack.end() - argCount, stack.end());
    Value result = native->function(*this, args);
    truncateStack(stack.size() - static_cast<size_t>(argCount) - 1);
    push(result);
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
    frame.resultIndex = frame.stackBase - 1;
    frames.push_back(frame);
}

void Vm::callMethod(ObjClosure* method, int argCount, bool isInitializer) {
    if (argCount != method->function->arity) {
        runtimeError("Expected " + std::to_string(method->function->arity) + " argument(s) but got " +
                     std::to_string(argCount) + ".");
    }
    if (frames.size() >= kMaxFrames) {
        runtimeError("Stack overflow.");
    }

    CallFrame frame;
    frame.closure = method;
    frame.ip = 0;
    // `self` occupies slot 0 (already placed there by the caller -- see
    // `callValue`'s BoundMethod case and `callClass`), so, unlike a plain
    // call, there's no separate callee slot *below* stackBase here.
    frame.stackBase = stack.size() - static_cast<size_t>(argCount) - 1;
    frame.resultIndex = frame.stackBase;
    frame.isInitializer = isInitializer;
    frames.push_back(frame);
}

void Vm::callClass(ObjClass* klass, int argCount) {
    size_t calleeIndex = stack.size() - static_cast<size_t>(argCount) - 1;
    auto* instance = allocate<ObjInstance>(klass);
    // Overwrites the class value's own slot -- mirrors `poc`'s
    // `IqaloxClass.call`, which always returns the freshly created
    // instance regardless of what (if anything) `init` itself returns.
    stack[calleeIndex] = objValue(instance);

    auto it = klass->methods.find("init");
    if (it == klass->methods.end()) {
        if (argCount != 0) {
            runtimeError("Expected 0 argument(s) but got " + std::to_string(argCount) + ".");
        }
        // No frame to push -- `stack[calleeIndex]` already holds the
        // finished call's result (the instance), and there are no
        // leftover arguments to discard.
        return;
    }
    callMethod(it->second, argCount, /*isInitializer=*/true);
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

Value Vm::bindMethod(ObjClass* klass, const Value& receiver, const std::string& name) {
    auto it = klass->methods.find(name);
    if (it == klass->methods.end()) {
        runtimeError("Undefined property '" + name + "'.");
    }
    return objValue(allocate<ObjBoundMethod>(receiver, it->second));
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
        // Captured before reading the opcode/operand bytes below, so
        // `runtimeError` can still find this instruction's own line after
        // `ip` has advanced past it (or after a callee this instruction
        // invoked has pushed further frames of its own -- `frames.back()`
        // during a callee's own arity/stack-overflow check is still this
        // frame, since that check runs before the new one is pushed).
        frame.currentInstructionIp = frame.ip;
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
                if (frame.isInitializer) {
                    // Always the instance, regardless of what `init`
                    // itself returned -- still sitting at `resultIndex`,
                    // where `callClass` placed it before this frame ever
                    // started running (see `CallFrame::isInitializer`).
                    result = stack[frame.resultIndex];
                }
                size_t resultIndex = frame.resultIndex;
                truncateStack(resultIndex);
                frames.pop_back();
                if (frames.empty()) {
                    return;
                }
                push(result);
                break;
            }
            case OpCode::Class: {
                ObjString* name = stringConstantAt(frame, readU16(frame));
                push(objValue(allocate<ObjClass>(name->value)));
                break;
            }
            case OpCode::Method: {
                ObjString* name = stringConstantAt(frame, readU16(frame));
                auto* closure = static_cast<ObjClosure*>(asObj(pop()));
                static_cast<ObjClass*>(asObj(peek(0)))->methods[name->value] = closure;
                break;
            }
            case OpCode::Inherit: {
                const Value& superclassValue = peek(1);
                if (!isObj(superclassValue) || asObj(superclassValue)->type != ObjType::Class) {
                    runtimeError("Superclass must be a class.");
                }
                auto* superclass = static_cast<ObjClass*>(asObj(superclassValue));
                auto* subclass = static_cast<ObjClass*>(asObj(peek(0)));
                // Copies the superclass's methods in *now*, before any of
                // the subclass's own `Method` opcodes run -- so a
                // same-named subclass method naturally overrides the
                // inherited entry, and `find`-by-name never has to walk a
                // superclass chain at all (see `ObjClass`'s doc comment).
                subclass->methods = superclass->methods;
                break;
            }
            case OpCode::GetProperty: {
                ObjString* name = stringConstantAt(frame, readU16(frame));
                Value receiver = pop();
                if (!isObj(receiver) || asObj(receiver)->type != ObjType::Instance) {
                    runtimeError("Only instances have properties.");
                }
                auto* instance = static_cast<ObjInstance*>(asObj(receiver));
                auto fieldIt = instance->fields.find(name->value);
                if (fieldIt != instance->fields.end()) {
                    push(fieldIt->second);
                } else {
                    push(bindMethod(instance->klass, receiver, name->value));
                }
                break;
            }
            case OpCode::SetProperty: {
                ObjString* name = stringConstantAt(frame, readU16(frame));
                Value value = pop();
                Value receiver = pop();
                if (!isObj(receiver) || asObj(receiver)->type != ObjType::Instance) {
                    runtimeError("Only instances have fields.");
                }
                static_cast<ObjInstance*>(asObj(receiver))->fields[name->value] = value;
                push(value);
                break;
            }
            case OpCode::GetSuper: {
                ObjString* name = stringConstantAt(frame, readU16(frame));
                auto* superclass = static_cast<ObjClass*>(asObj(pop()));
                Value self = pop();
                push(bindMethod(superclass, self, name->value));
                break;
            }
            case OpCode::GetIndex: {
                Value indexValue = pop();
                Value receiver = pop();
                size_t index = checkVectorIndex(receiver, indexValue);
                push(static_cast<ObjVector*>(asObj(receiver))->elements[index]);
                break;
            }
            case OpCode::SetIndex: {
                Value value = pop();
                Value indexValue = pop();
                Value receiver = pop();
                size_t index = checkVectorIndex(receiver, indexValue);
                static_cast<ObjVector*>(asObj(receiver))->elements[index] = value;
                push(value);
                break;
            }
            case OpCode::VectorLength: {
                Value receiver = pop();
                if (!isObj(receiver) || asObj(receiver)->type != ObjType::Vector) {
                    runtimeError("Only vectors have a length, got " + typeName(receiver) + ".");
                }
                push(numberValue(static_cast<double>(static_cast<ObjVector*>(asObj(receiver))->elements.size())));
                break;
            }
            case OpCode::VectorAppend: {
                Value value = pop();
                Value receiver = pop();
                // Only ever emitted inside the synthetic closures
                // Cons/ListComprehension desugar into (docs/PLAN-0.2.md
                // Phase 3), appending onto an accumulator built with
                // BuildVector just before -- never reachable with a
                // non-vector receiver from any user-written program, so
                // this is an internal-consistency check, not a
                // user-facing type error.
                if (!isObj(receiver) || asObj(receiver)->type != ObjType::Vector) {
                    runtimeError("Internal error: VectorAppend on a non-vector.");
                }
                static_cast<ObjVector*>(asObj(receiver))->elements.push_back(value);
                break;
            }
            case OpCode::VectorExtend: {
                // docs/PLAN-0.2.md Phase 4: `[..., ...source, ...]`
                // -- unlike VectorAppend, `source` is a user-written
                // expression, so a non-vector source is a real,
                // user-facing runtime error, not an internal-consistency
                // check. Pushes the target back (unlike VectorAppend):
                // Codegen.fs's spread-flattening chains several of these
                // purely on the stack, with no accumulator local slot to
                // re-fetch it from in between.
                Value source = pop();
                Value target = pop();
                if (!isObj(source) || asObj(source)->type != ObjType::Vector) {
                    runtimeError("Can only spread a vector, got " + typeName(source) + ".");
                }
                auto& targetElements = static_cast<ObjVector*>(asObj(target))->elements;
                auto& sourceElements = static_cast<ObjVector*>(asObj(source))->elements;
                targetElements.insert(targetElements.end(), sourceElements.begin(), sourceElements.end());
                push(target);
                break;
            }
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
        case ObjType::NativeFunction:
            // A raw C++ function pointer plus a name/arity -- nothing
            // GC-owned to trace through.
            break;
        case ObjType::Class:
            for (auto& [name, closure] : static_cast<ObjClass*>(obj)->methods) markObject(closure);
            break;
        case ObjType::Instance: {
            auto* instance = static_cast<ObjInstance*>(obj);
            markObject(instance->klass);
            for (auto& [name, value] : instance->fields) markValue(value);
            break;
        }
        case ObjType::BoundMethod: {
            auto* bound = static_cast<ObjBoundMethod*>(obj);
            markValue(bound->receiver);
            markObject(bound->method);
            break;
        }
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

#pragma once

#include <cstdint>
#include <string>
#include <vector>

#include "value.hpp"

namespace iqalox {

enum class ObjType : uint8_t {
    String,
    Vector,
    Function,
    Closure,
    Upvalue,
};

// Base of every heap-allocated, GC-managed value. `next` is the intrusive
// singly-linked list of *every* live allocation -- see `Vm::allocate`/
// `Vm::sweep` -- distinct from `ObjUpvalue::nextOpen`'s separate list of
// just the currently-*open* upvalues.
struct Obj {
    ObjType type;
    bool marked = false;
    Obj* next = nullptr;

    explicit Obj(ObjType t) : type(t) {}
    // Virtual so `delete` through a base `Obj*` (all the GC's sweep phase
    // ever holds) correctly runs the derived destructor.
    virtual ~Obj() = default;
};

struct ObjString : Obj {
    std::string value;
    explicit ObjString(std::string v) : Obj(ObjType::String), value(std::move(v)) {}
};

struct ObjVector : Obj {
    std::vector<Value> elements;
    ObjVector() : Obj(ObjType::Vector) {}
};

// How a closure captures one of its upvalues when it's created at
// runtime -- mirrors `compiler/src/Bound.fs`'s `UpvalueDescriptor` exactly
// (see `compiler/src/Bytecode.fs`'s format doc comment for the on-disk
// encoding this is read from).
struct UpvalueDescriptor {
    bool fromEnclosingLocal;
    uint16_t index;
};

// The structured, in-memory form of one `compiler/src/Bytecode.fs` chunk:
// a raw instruction byte stream plus its constant pool, already resolved
// to runtime `Value`s (numbers/strings/nested functions) at load time --
// see `bytecode::load`. Unlike the F# compiler side, there's no need for a
// separate structured `Instruction` representation here: the VM decodes
// opcodes directly off `code` as it executes, the same way `clox` does.
struct Chunk {
    std::vector<uint8_t> code;
    std::vector<Value> constants;
};

struct ObjFunction : Obj {
    int arity = 0;
    // Not load-bearing for execution (slot indices are always relative to
    // a frame's own base, and the instruction stream already encodes
    // exactly how many slots to push/pop) -- kept for fidelity to the
    // format and future diagnostics.
    int localCount = 0;
    std::string name;
    std::vector<UpvalueDescriptor> upvalues;
    Chunk chunk;

    ObjFunction() : Obj(ObjType::Function) {}
};

// While open, `stackIndex` names the VM stack slot this upvalue aliases
// (so a write through the upvalue is visible to the local, and vice
// versa); `Vm::closeUpvalues` copies that slot's value into `closed` and
// flips `open` to false once the slot is about to be reclaimed, so the
// captured value survives independently of the stack after that.
//
// An index rather than a `Value*` into the stack deliberately: the VM's
// stack is a `std::deque` (see `vm.hpp` for why), and comparing pointers
// into two different elements of a non-contiguous container with
// `<`/`>=` -- which capturing/closing both need, to find/close every
// upvalue at or above a given stack position -- is unspecified behavior
// unless they alias the same underlying array. Plain size_t comparisons
// have no such hazard.
struct ObjUpvalue : Obj {
    size_t stackIndex;
    bool open = true;
    Value closed = NilValue;
    ObjUpvalue* nextOpen = nullptr;

    explicit ObjUpvalue(size_t index) : Obj(ObjType::Upvalue), stackIndex(index) {}
};

struct ObjClosure : Obj {
    ObjFunction* function;
    std::vector<ObjUpvalue*> upvalues;

    explicit ObjClosure(ObjFunction* fn) : Obj(ObjType::Closure), function(fn) {}
};

}  // namespace iqalox

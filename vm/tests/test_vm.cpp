#include <catch2/catch_test_macros.hpp>

#include <iostream>
#include <sstream>
#include <string>
#include <vector>

#include "bytecode.hpp"
#include "chunk_builder.hpp"
#include "vm.hpp"

using namespace iqalox;
using bytecode::OpCode;
using iqalox::testing::ChunkBuilder;

TEST_CASE("arithmetic evaluates and a global holds the result", "[vm]") {
    Vm vm;
    ChunkBuilder b(vm);
    uint16_t one = b.addNumberConstant(1);
    uint16_t two = b.addNumberConstant(2);
    uint16_t three = b.addNumberConstant(3);
    uint16_t result = b.addStringConstant("result");

    // result = 1 + 2 * 3
    b.emitU16(OpCode::Constant, one);
    b.emitU16(OpCode::Constant, two);
    b.emitU16(OpCode::Constant, three);
    b.emit(OpCode::Multiply);
    b.emit(OpCode::Add);
    b.emitU16(OpCode::DefineGlobal, result);

    vm.interpret(b.build());

    REQUIRE(vm.getGlobal("result") != nullptr);
    REQUIRE(asNumber(*vm.getGlobal("result")) == 7.0);
}

TEST_CASE("modulo and power match Python's floored-modulo semantics, not C's fmod", "[vm]") {
    Vm vm;
    ChunkBuilder b(vm);
    uint16_t negOne = b.addNumberConstant(-1);
    uint16_t four = b.addNumberConstant(4);
    uint16_t result = b.addStringConstant("result");

    b.emitU16(OpCode::Constant, negOne);
    b.emitU16(OpCode::Constant, four);
    b.emit(OpCode::Modulo);
    b.emitU16(OpCode::DefineGlobal, result);

    vm.interpret(b.build());

    REQUIRE(asNumber(*vm.getGlobal("result")) == 3.0);  // Python: -1 % 4 == 3, not fmod's -1
}

TEST_CASE("comparisons and equality produce booleans", "[vm]") {
    Vm vm;
    ChunkBuilder b(vm);
    uint16_t one = b.addNumberConstant(1);
    uint16_t two = b.addNumberConstant(2);
    uint16_t result = b.addStringConstant("result");

    b.emitU16(OpCode::Constant, one);
    b.emitU16(OpCode::Constant, two);
    b.emit(OpCode::Less);
    b.emitU16(OpCode::DefineGlobal, result);

    vm.interpret(b.build());

    REQUIRE(isBool(*vm.getGlobal("result")));
    REQUIRE(asBool(*vm.getGlobal("result")) == true);
}

TEST_CASE("only nil and false are falsy -- 0 is truthy", "[vm]") {
    Vm vm;
    ChunkBuilder b(vm);
    uint16_t zero = b.addNumberConstant(0);
    uint16_t result = b.addStringConstant("result");

    b.emitU16(OpCode::Constant, zero);
    b.emit(OpCode::Not);
    b.emitU16(OpCode::DefineGlobal, result);

    vm.interpret(b.build());

    REQUIRE(asBool(*vm.getGlobal("result")) == false);  // !0 is false: 0 is truthy
}

TEST_CASE("strings compare equal by content, not identity", "[vm]") {
    Vm vm;
    ChunkBuilder b(vm);
    uint16_t a = b.addStringConstant("hi");
    uint16_t bConst = b.addStringConstant("hi");  // a second, distinct ObjString with the same content
    uint16_t result = b.addStringConstant("result");

    b.emitU16(OpCode::Constant, a);
    b.emitU16(OpCode::Constant, bConst);
    b.emit(OpCode::Equal);
    b.emitU16(OpCode::DefineGlobal, result);

    vm.interpret(b.build());

    REQUIRE(asBool(*vm.getGlobal("result")) == true);
}

TEST_CASE("vectors compare equal element-wise and recursively", "[vm]") {
    Vm vm;
    ChunkBuilder b(vm);
    uint16_t one = b.addNumberConstant(1);
    uint16_t two = b.addNumberConstant(2);
    uint16_t three = b.addNumberConstant(3);
    uint16_t result = b.addStringConstant("result");

    b.emitU16(OpCode::Constant, one);
    b.emitU16(OpCode::Constant, two);
    b.emitU16(OpCode::BuildVector, 2);  // [1, 2]
    b.emitU16(OpCode::Constant, one);
    b.emitU16(OpCode::Constant, three);
    b.emitU16(OpCode::BuildVector, 2);  // [1, 3]
    b.emit(OpCode::NotEqual);
    b.emitU16(OpCode::DefineGlobal, result);

    vm.interpret(b.build());

    REQUIRE(asBool(*vm.getGlobal("result")) == true);  // [1, 2] != [1, 3]
}

TEST_CASE("reading an Undef local is a runtime error", "[vm]") {
    Vm vm;
    ChunkBuilder b(vm);
    b.emit(OpCode::Undef);           // slot 0, a `mut`-without-initializer
    b.emitU16(OpCode::GetLocal, 0);  // reading it before assignment

    REQUIRE_THROWS_AS(vm.interpret(b.build()), RuntimeError);
}

TEST_CASE("reading an undefined global is a runtime error", "[vm]") {
    Vm vm;
    ChunkBuilder b(vm);
    uint16_t name = b.addStringConstant("neverDeclared");
    b.emitU16(OpCode::GetGlobal, name);

    REQUIRE_THROWS_AS(vm.interpret(b.build()), RuntimeError);
}

TEST_CASE("assigning to an undeclared global is a runtime error", "[vm]") {
    Vm vm;
    ChunkBuilder b(vm);
    uint16_t one = b.addNumberConstant(1);
    uint16_t name = b.addStringConstant("neverDeclared");
    b.emitU16(OpCode::Constant, one);
    b.emitU16(OpCode::SetGlobal, name);

    REQUIRE_THROWS_AS(vm.interpret(b.build()), RuntimeError);
}

TEST_CASE("division by zero is a runtime error", "[vm]") {
    Vm vm;
    ChunkBuilder b(vm);
    uint16_t one = b.addNumberConstant(1);
    uint16_t zero = b.addNumberConstant(0);
    b.emitU16(OpCode::Constant, one);
    b.emitU16(OpCode::Constant, zero);
    b.emit(OpCode::Divide);

    REQUIRE_THROWS_AS(vm.interpret(b.build()), RuntimeError);
}

TEST_CASE("a runtime error reports the source line the faulting instruction came from", "[vm]") {
    // Regression test for the diagnostics gap found while writing
    // docs/LANGUAGE.md's Phase 10 entry: runtime errors used to carry no
    // line number at all. `Chunk::lines` is one u16 per *byte* of `code`
    // (docs/PLAN-0.1.md's Phase 10 entry) -- hand-populated here since
    // ChunkBuilder, unlike the real compiler, has no source lines of its
    // own to derive them from.
    Vm vm;
    ChunkBuilder b(vm);
    uint16_t one = b.addNumberConstant(1);
    uint16_t zero = b.addNumberConstant(0);
    b.emitU16(OpCode::Constant, one);  // bytes 0-2
    b.emitU16(OpCode::Constant, zero);  // bytes 3-5
    b.emit(OpCode::Divide);  // byte 6

    auto* fn = b.build();
    fn->chunk.lines.assign(fn->chunk.code.size(), 1);
    fn->chunk.lines[6] = 42;  // Divide's own opcode byte

    try {
        vm.interpret(fn);
        FAIL("expected a RuntimeError");
    } catch (const RuntimeError& error) {
        REQUIRE(std::string(error.what()).find("[line 42]") != std::string::npos);
    }
}

TEST_CASE("adding a non-number is a runtime error", "[vm]") {
    Vm vm;
    ChunkBuilder b(vm);
    uint16_t one = b.addNumberConstant(1);
    b.emitU16(OpCode::Constant, one);
    b.emit(OpCode::True);
    b.emit(OpCode::Add);

    REQUIRE_THROWS_AS(vm.interpret(b.build()), RuntimeError);
}

TEST_CASE("calling a non-callable value is a runtime error", "[vm]") {
    Vm vm;
    ChunkBuilder b(vm);
    uint16_t one = b.addNumberConstant(1);
    b.emitU16(OpCode::Constant, one);
    b.emitU16(OpCode::Call, 0);

    REQUIRE_THROWS_AS(vm.interpret(b.build()), RuntimeError);
}

TEST_CASE("calling a closure with the wrong argument count is a runtime error", "[vm]") {
    Vm vm;
    ChunkBuilder fnBuilder(vm);
    fnBuilder.emit(OpCode::Nil);
    fnBuilder.emit(OpCode::Return);
    ObjFunction* fn = fnBuilder.build(1, "f");  // expects 1 argument

    ChunkBuilder b(vm);
    uint16_t fnIndex = b.addFunctionConstant(fn);
    b.emitClosure(fnIndex, {});
    b.emitU16(OpCode::Call, 0);  // called with none

    REQUIRE_THROWS_AS(vm.interpret(b.build()), RuntimeError);
}

TEST_CASE("a jump-based conditional (elvis-style) selects the correct branch", "[vm]") {
    Vm vm;
    ChunkBuilder b(vm);
    uint16_t thenVal = b.addNumberConstant(1);
    uint16_t elseVal = b.addNumberConstant(2);
    uint16_t result = b.addStringConstant("result");

    b.emit(OpCode::False);
    size_t jumpToElse = b.emitJump(OpCode::JumpIfFalse);
    b.emit(OpCode::Pop);
    b.emitU16(OpCode::Constant, thenVal);
    size_t jumpToEnd = b.emitJump(OpCode::Jump);
    b.patch(jumpToElse);
    b.emit(OpCode::Pop);
    b.emitU16(OpCode::Constant, elseVal);
    b.patch(jumpToEnd);
    b.emitU16(OpCode::DefineGlobal, result);

    vm.interpret(b.build());

    REQUIRE(asNumber(*vm.getGlobal("result")) == 2.0);
}

TEST_CASE("a function call passes arguments and returns a value", "[vm]") {
    Vm vm;
    // fun addOne(a) { return a + 1; }
    ChunkBuilder fnBuilder(vm);
    uint16_t one = fnBuilder.addNumberConstant(1);
    fnBuilder.emitU16(OpCode::GetLocal, 0);
    fnBuilder.emitU16(OpCode::Constant, one);
    fnBuilder.emit(OpCode::Add);
    fnBuilder.emit(OpCode::Return);
    ObjFunction* fn = fnBuilder.build(1, "addOne");

    ChunkBuilder b(vm);
    uint16_t fnIndex = b.addFunctionConstant(fn);
    uint16_t five = b.addNumberConstant(5);
    uint16_t result = b.addStringConstant("result");

    b.emitClosure(fnIndex, {});
    b.emitU16(OpCode::Constant, five);
    b.emitU16(OpCode::Call, 1);
    b.emitU16(OpCode::DefineGlobal, result);

    vm.interpret(b.build());

    REQUIRE(asNumber(*vm.getGlobal("result")) == 6.0);
}

TEST_CASE("a closure captures an enclosing local and outlives the frame that declared it", "[vm]") {
    Vm vm;
    // fun makeGetter() { var x mut = 42; fun get() { return x; } return get; }
    ChunkBuilder getBuilder(vm);
    getBuilder.emitU16(OpCode::GetUpvalue, 0);
    getBuilder.emit(OpCode::Return);
    ObjFunction* getFn = getBuilder.build(0, "get");

    ChunkBuilder makeGetterBuilder(vm);
    uint16_t fortyTwo = makeGetterBuilder.addNumberConstant(42);
    uint16_t getIndex = makeGetterBuilder.addFunctionConstant(getFn);
    makeGetterBuilder.emitU16(OpCode::Constant, fortyTwo);  // slot 0: x = 42
    makeGetterBuilder.emitClosure(getIndex, {{true, 0}});   // captures slot 0 directly
    makeGetterBuilder.emit(OpCode::Return);                 // returns the closure; x's slot is torn down
    ObjFunction* makeGetterFn = makeGetterBuilder.build(0, "makeGetter");

    ChunkBuilder b(vm);
    uint16_t makeGetterIndex = b.addFunctionConstant(makeGetterFn);
    uint16_t result = b.addStringConstant("result");

    b.emitClosure(makeGetterIndex, {});
    b.emitU16(OpCode::Call, 0);  // -> the `get` closure
    b.emitU16(OpCode::Call, 0);  // -> 42, read back after makeGetter's frame is long gone
    b.emitU16(OpCode::DefineGlobal, result);

    vm.interpret(b.build());

    REQUIRE(asNumber(*vm.getGlobal("result")) == 42.0);
}

TEST_CASE("two closures over the same local share one upvalue -- a write through one is visible to the other",
          "[vm]") {
    Vm vm;
    // fun increment() { x = x + 1; return x; }  -- reads/writes the shared upvalue
    ChunkBuilder incBuilder(vm);
    uint16_t one = incBuilder.addNumberConstant(1);
    incBuilder.emitU16(OpCode::GetUpvalue, 0);
    incBuilder.emitU16(OpCode::Constant, one);
    incBuilder.emit(OpCode::Add);
    incBuilder.emitU16(OpCode::SetUpvalue, 0);
    incBuilder.emit(OpCode::Pop);
    incBuilder.emitU16(OpCode::GetUpvalue, 0);
    incBuilder.emit(OpCode::Return);
    ObjFunction* incFn = incBuilder.build(0, "increment");

    // fun makeCounter() { var x mut = 0; fun increment() {...}; return increment; }
    ChunkBuilder makeCounterBuilder(vm);
    uint16_t zero = makeCounterBuilder.addNumberConstant(0);
    uint16_t incIndex = makeCounterBuilder.addFunctionConstant(incFn);
    makeCounterBuilder.emitU16(OpCode::Constant, zero);  // slot 0: x = 0
    makeCounterBuilder.emitClosure(incIndex, {{true, 0}});
    makeCounterBuilder.emit(OpCode::Return);
    ObjFunction* makeCounterFn = makeCounterBuilder.build(0, "makeCounter");

    ChunkBuilder b(vm);
    uint16_t makeCounterIndex = b.addFunctionConstant(makeCounterFn);
    uint16_t result = b.addStringConstant("result");

    b.emitClosure(makeCounterIndex, {});
    b.emitU16(OpCode::Call, 0);      // slot 0: the `increment` closure, a top-level local
    b.emitU16(OpCode::GetLocal, 0);
    b.emitU16(OpCode::Call, 0);      // first call -> 1 (discarded)
    b.emit(OpCode::Pop);
    b.emitU16(OpCode::GetLocal, 0);
    b.emitU16(OpCode::Call, 0);      // second call -> 2 (kept): sees the first call's write
    b.emitU16(OpCode::DefineGlobal, result);

    vm.interpret(b.build());

    REQUIRE(asNumber(*vm.getGlobal("result")) == 2.0);
}

TEST_CASE("aggressive stress-mode collection doesn't corrupt a running program", "[vm][gc]") {
    Vm vm;
    vm.stressGc = true;

    // Allocates a handful of strings, functions/closures, and vectors --
    // one of everything the GC has to trace -- interleaved with ordinary
    // arithmetic, with a collection forced before *every single*
    // allocation. If any of them isn't correctly rooted, this either
    // crashes or produces a wrong final answer.
    ChunkBuilder fnBuilder(vm);
    fnBuilder.emitU16(OpCode::GetUpvalue, 0);
    fnBuilder.emit(OpCode::Return);
    ObjFunction* getFn = fnBuilder.build(0, "get");

    ChunkBuilder b(vm);
    uint16_t fortyTwo = b.addNumberConstant(42);
    uint16_t one = b.addNumberConstant(1);
    uint16_t two = b.addNumberConstant(2);
    uint16_t getIndex = b.addFunctionConstant(getFn);
    uint16_t label = b.addStringConstant("a label, just to allocate a string");
    uint16_t result = b.addStringConstant("result");

    b.emitU16(OpCode::Constant, fortyTwo);         // slot 0: x = 42
    b.emitClosure(getIndex, {{true, 0}});          // a closure over it
    b.emitU16(OpCode::Call, 0);                    // -> 42
    b.emitU16(OpCode::Constant, label);
    b.emit(OpCode::Pop);                           // just to allocate + discard a string
    b.emitU16(OpCode::Constant, one);
    b.emitU16(OpCode::Constant, two);
    b.emitU16(OpCode::BuildVector, 2);             // [1, 2], also discarded
    b.emit(OpCode::Pop);
    b.emit(OpCode::Add);                           // 42 (from the closure call) + x (slot 0)
    b.emitU16(OpCode::DefineGlobal, result);

    vm.interpret(b.build());

    REQUIRE(asNumber(*vm.getGlobal("result")) == 84.0);
}

TEST_CASE("print and concat are present as globals without any user declaration", "[vm][natives]") {
    Vm vm;
    REQUIRE(vm.getGlobal("print") != nullptr);
    REQUIRE(vm.getGlobal("concat") != nullptr);
}

TEST_CASE("print writes stringify(value) plus a newline to stdout and returns nil", "[vm][natives]") {
    Vm vm;
    ChunkBuilder b(vm);
    uint16_t printName = b.addStringConstant("print");
    uint16_t hello = b.addStringConstant("hello");
    uint16_t result = b.addStringConstant("result");

    b.emitU16(OpCode::GetGlobal, printName);
    b.emitU16(OpCode::Constant, hello);
    b.emitU16(OpCode::Call, 1);
    b.emitU16(OpCode::DefineGlobal, result);

    std::ostringstream captured;
    std::streambuf* oldBuf = std::cout.rdbuf(captured.rdbuf());
    vm.interpret(b.build());
    std::cout.rdbuf(oldBuf);

    REQUIRE(captured.str() == "hello\n");
    REQUIRE(isNil(*vm.getGlobal("result")));
}

TEST_CASE("concat joins stringify of each element with no separator", "[vm][natives]") {
    Vm vm;
    ChunkBuilder b(vm);
    uint16_t concatName = b.addStringConstant("concat");
    uint16_t one = b.addNumberConstant(1);
    uint16_t a = b.addStringConstant("a");
    uint16_t result = b.addStringConstant("result");

    b.emitU16(OpCode::GetGlobal, concatName);
    b.emitU16(OpCode::Constant, one);
    b.emitU16(OpCode::Constant, a);
    b.emit(OpCode::True);
    b.emitU16(OpCode::BuildVector, 3);
    b.emitU16(OpCode::Call, 1);
    b.emitU16(OpCode::DefineGlobal, result);

    vm.interpret(b.build());

    const Value* resultValue = vm.getGlobal("result");
    REQUIRE(isObj(*resultValue));
    REQUIRE(static_cast<ObjString*>(asObj(*resultValue))->value == "1atrue");
}

TEST_CASE("concat with a non-vector argument is a clean runtime error, not a crash", "[vm][natives]") {
    // Regression test for a real poc bug (docs/PLAN-0.1-POC.md's running
    // list): poc's concat(5) raises an uncaught Python TypeError instead
    // of a clean IqaloxRuntimeError.
    Vm vm;
    ChunkBuilder b(vm);
    uint16_t concatName = b.addStringConstant("concat");
    uint16_t five = b.addNumberConstant(5);

    b.emitU16(OpCode::GetGlobal, concatName);
    b.emitU16(OpCode::Constant, five);
    b.emitU16(OpCode::Call, 1);

    REQUIRE_THROWS_AS(vm.interpret(b.build()), RuntimeError);
}

TEST_CASE("calling print with the wrong argument count is a runtime error", "[vm][natives]") {
    Vm vm;
    ChunkBuilder b(vm);
    uint16_t printName = b.addStringConstant("print");

    b.emitU16(OpCode::GetGlobal, printName);
    b.emitU16(OpCode::Call, 0);

    REQUIRE_THROWS_AS(vm.interpret(b.build()), RuntimeError);
}

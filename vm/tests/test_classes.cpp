#include <catch2/catch_test_macros.hpp>

#include <string>

#include "chunk_builder.hpp"
#include "object.hpp"
#include "vm.hpp"

using namespace iqalox;
using bytecode::OpCode;
using iqalox::testing::ChunkBuilder;

namespace {

std::string asString(const Value& v) {
    REQUIRE(isObj(v));
    REQUIRE(asObj(v)->type == ObjType::String);
    return static_cast<ObjString*>(asObj(v))->value;
}

}  // namespace

TEST_CASE("a class with no init constructs a bare instance, and fields get/set", "[vm][classes]") {
    Vm vm;
    ChunkBuilder b(vm);
    uint16_t pointName = b.addStringConstant("Point");
    uint16_t pName = b.addStringConstant("p");
    uint16_t xName = b.addStringConstant("x");
    uint16_t five = b.addNumberConstant(5);
    uint16_t resultName = b.addStringConstant("result");

    b.emitU16(OpCode::Class, pointName);
    b.emitU16(OpCode::DefineGlobal, pointName);

    b.emitU16(OpCode::GetGlobal, pointName);
    b.emitU16(OpCode::Call, 0);
    b.emitU16(OpCode::DefineGlobal, pName);

    b.emitU16(OpCode::GetGlobal, pName);
    b.emitU16(OpCode::Constant, five);
    b.emitU16(OpCode::SetProperty, xName);
    b.emit(OpCode::Pop);

    b.emitU16(OpCode::GetGlobal, pName);
    b.emitU16(OpCode::GetProperty, xName);
    b.emitU16(OpCode::DefineGlobal, resultName);

    vm.interpret(b.build());

    REQUIRE(asNumber(*vm.getGlobal("result")) == 5.0);
}

TEST_CASE("init runs with self bound and its own return value is discarded for the instance", "[vm][classes]") {
    // class Point { init(x, y) { self.x = x; self.y = y; } sum() { return self.x + self.y; } }
    // var p = Point(3, 4); var result = p.sum();
    Vm vm;

    ChunkBuilder initBuilder(vm);
    uint16_t xName1 = initBuilder.addStringConstant("x");
    uint16_t yName1 = initBuilder.addStringConstant("y");
    initBuilder.emitU16(OpCode::GetLocal, 0);  // self
    initBuilder.emitU16(OpCode::GetLocal, 1);  // x
    initBuilder.emitU16(OpCode::SetProperty, xName1);
    initBuilder.emit(OpCode::Pop);
    initBuilder.emitU16(OpCode::GetLocal, 0);  // self
    initBuilder.emitU16(OpCode::GetLocal, 2);  // y
    initBuilder.emitU16(OpCode::SetProperty, yName1);
    initBuilder.emit(OpCode::Pop);
    ObjFunction* initFn = initBuilder.build(2, "init");

    ChunkBuilder sumBuilder(vm);
    uint16_t xName2 = sumBuilder.addStringConstant("x");
    uint16_t yName2 = sumBuilder.addStringConstant("y");
    sumBuilder.emitU16(OpCode::GetLocal, 0);
    sumBuilder.emitU16(OpCode::GetProperty, xName2);
    sumBuilder.emitU16(OpCode::GetLocal, 0);
    sumBuilder.emitU16(OpCode::GetProperty, yName2);
    sumBuilder.emit(OpCode::Add);
    sumBuilder.emit(OpCode::Return);
    ObjFunction* sumFn = sumBuilder.build(0, "sum");

    ChunkBuilder b(vm);
    uint16_t pointName = b.addStringConstant("Point");
    uint16_t initIndex = b.addFunctionConstant(initFn);
    uint16_t sumIndex = b.addFunctionConstant(sumFn);
    uint16_t initName = b.addStringConstant("init");
    uint16_t sumName = b.addStringConstant("sum");
    uint16_t three = b.addNumberConstant(3);
    uint16_t four = b.addNumberConstant(4);
    uint16_t pName = b.addStringConstant("p");
    uint16_t resultName = b.addStringConstant("result");

    b.emitU16(OpCode::Class, pointName);
    b.emitU16(OpCode::DefineGlobal, pointName);
    b.emitU16(OpCode::GetGlobal, pointName);  // temp re-fetch
    b.emitClosure(initIndex, {});
    b.emitU16(OpCode::Method, initName);
    b.emitClosure(sumIndex, {});
    b.emitU16(OpCode::Method, sumName);
    b.emit(OpCode::Pop);  // discard temp re-fetch

    b.emitU16(OpCode::GetGlobal, pointName);
    b.emitU16(OpCode::Constant, three);
    b.emitU16(OpCode::Constant, four);
    b.emitU16(OpCode::Call, 2);
    b.emitU16(OpCode::DefineGlobal, pName);

    b.emitU16(OpCode::GetGlobal, pName);
    b.emitU16(OpCode::GetProperty, sumName);
    b.emitU16(OpCode::Call, 0);
    b.emitU16(OpCode::DefineGlobal, resultName);

    vm.interpret(b.build());

    REQUIRE(asNumber(*vm.getGlobal("result")) == 7.0);
}

TEST_CASE("calling a class with the wrong argument count for init is a runtime error", "[vm][classes]") {
    Vm vm;
    ChunkBuilder initBuilder(vm);
    ObjFunction* initFn = initBuilder.build(1, "init");  // expects 1 argument

    ChunkBuilder b(vm);
    uint16_t pointName = b.addStringConstant("Point");
    uint16_t initIndex = b.addFunctionConstant(initFn);
    uint16_t initName = b.addStringConstant("init");

    b.emitU16(OpCode::Class, pointName);
    b.emitU16(OpCode::DefineGlobal, pointName);
    b.emitU16(OpCode::GetGlobal, pointName);
    b.emitClosure(initIndex, {});
    b.emitU16(OpCode::Method, initName);
    b.emit(OpCode::Pop);

    b.emitU16(OpCode::GetGlobal, pointName);
    b.emitU16(OpCode::Call, 0);  // no arguments given

    REQUIRE_THROWS_AS(vm.interpret(b.build()), RuntimeError);
}

TEST_CASE("calling a class with no init and any arguments is a runtime error", "[vm][classes]") {
    Vm vm;
    ChunkBuilder b(vm);
    uint16_t pointName = b.addStringConstant("Point");
    uint16_t one = b.addNumberConstant(1);

    b.emitU16(OpCode::Class, pointName);
    b.emitU16(OpCode::DefineGlobal, pointName);

    b.emitU16(OpCode::GetGlobal, pointName);
    b.emitU16(OpCode::Constant, one);
    b.emitU16(OpCode::Call, 1);

    REQUIRE_THROWS_AS(vm.interpret(b.build()), RuntimeError);
}

TEST_CASE("reading an undefined property is a runtime error", "[vm][classes]") {
    Vm vm;
    ChunkBuilder b(vm);
    uint16_t pointName = b.addStringConstant("Point");
    uint16_t pName = b.addStringConstant("p");
    uint16_t missing = b.addStringConstant("missing");

    b.emitU16(OpCode::Class, pointName);
    b.emitU16(OpCode::DefineGlobal, pointName);
    b.emitU16(OpCode::GetGlobal, pointName);
    b.emitU16(OpCode::Call, 0);
    b.emitU16(OpCode::DefineGlobal, pName);

    b.emitU16(OpCode::GetGlobal, pName);
    b.emitU16(OpCode::GetProperty, missing);

    REQUIRE_THROWS_AS(vm.interpret(b.build()), RuntimeError);
}

TEST_CASE("getting a property on a non-instance is a runtime error", "[vm][classes]") {
    Vm vm;
    ChunkBuilder b(vm);
    uint16_t five = b.addNumberConstant(5);
    uint16_t xName = b.addStringConstant("x");

    b.emitU16(OpCode::Constant, five);
    b.emitU16(OpCode::GetProperty, xName);

    REQUIRE_THROWS_AS(vm.interpret(b.build()), RuntimeError);
}

TEST_CASE("setting a property on a non-instance is a runtime error", "[vm][classes]") {
    Vm vm;
    ChunkBuilder b(vm);
    uint16_t five = b.addNumberConstant(5);
    uint16_t one = b.addNumberConstant(1);
    uint16_t xName = b.addStringConstant("x");

    b.emitU16(OpCode::Constant, five);
    b.emitU16(OpCode::Constant, one);
    b.emitU16(OpCode::SetProperty, xName);

    REQUIRE_THROWS_AS(vm.interpret(b.build()), RuntimeError);
}

TEST_CASE("extending a non-class value is a runtime error", "[vm][classes]") {
    Vm vm;
    ChunkBuilder b(vm);
    uint16_t notAClassName = b.addStringConstant("NotAClass");
    uint16_t five = b.addNumberConstant(5);
    uint16_t dogName = b.addStringConstant("Dog");

    b.emitU16(OpCode::Constant, five);
    b.emitU16(OpCode::DefineGlobal, notAClassName);

    b.emitU16(OpCode::Class, dogName);
    b.emitU16(OpCode::DefineGlobal, dogName);
    b.emitU16(OpCode::GetGlobal, notAClassName);
    b.emitU16(OpCode::GetGlobal, dogName);
    b.emit(OpCode::Inherit);

    REQUIRE_THROWS_AS(vm.interpret(b.build()), RuntimeError);
}

TEST_CASE("calling a non-callable instance is a runtime error", "[vm][classes]") {
    Vm vm;
    ChunkBuilder b(vm);
    uint16_t pointName = b.addStringConstant("Point");
    uint16_t pName = b.addStringConstant("p");

    b.emitU16(OpCode::Class, pointName);
    b.emitU16(OpCode::DefineGlobal, pointName);
    b.emitU16(OpCode::GetGlobal, pointName);
    b.emitU16(OpCode::Call, 0);
    b.emitU16(OpCode::DefineGlobal, pName);

    b.emitU16(OpCode::GetGlobal, pName);
    b.emitU16(OpCode::Call, 0);

    REQUIRE_THROWS_AS(vm.interpret(b.build()), RuntimeError);
}

TEST_CASE("inheritance: overriding, dynamic dispatch through self, and an explicit super call", "[vm][classes]") {
    // class Animal { speak() { return "..."; } describe() { return self.speak(); } }
    // class Dog extends Animal { speak() { return "Woof"; } describeSuper() { return super.speak(); } }
    // var d = Dog();
    // r1 = d.speak();          -- "Woof" (overridden)
    // r2 = d.describe();       -- "Woof" (inherited method, but dynamic dispatch on `self` finds Dog's override)
    // r3 = d.describeSuper();  -- "..." (explicit `super` bypasses the override)
    Vm vm;

    ChunkBuilder animalSpeakBuilder(vm);
    uint16_t dots = animalSpeakBuilder.addStringConstant("...");
    animalSpeakBuilder.emitU16(OpCode::Constant, dots);
    animalSpeakBuilder.emit(OpCode::Return);
    ObjFunction* animalSpeakFn = animalSpeakBuilder.build(0, "speak");

    ChunkBuilder describeBuilder(vm);
    uint16_t speakName1 = describeBuilder.addStringConstant("speak");
    describeBuilder.emitU16(OpCode::GetLocal, 0);  // self
    describeBuilder.emitU16(OpCode::GetProperty, speakName1);
    describeBuilder.emitU16(OpCode::Call, 0);
    describeBuilder.emit(OpCode::Return);
    ObjFunction* describeFn = describeBuilder.build(0, "describe");

    ChunkBuilder dogSpeakBuilder(vm);
    uint16_t woof = dogSpeakBuilder.addStringConstant("Woof");
    dogSpeakBuilder.emitU16(OpCode::Constant, woof);
    dogSpeakBuilder.emit(OpCode::Return);
    ObjFunction* dogSpeakFn = dogSpeakBuilder.build(0, "speak");

    // describeSuper(): self is slot 0, `super` is captured as upvalue 0
    // (mirrors how a method referencing `super` always resolves it -- see
    // Bound.BSuper / Resolver.fs).
    ChunkBuilder describeSuperBuilder(vm);
    uint16_t speakName2 = describeSuperBuilder.addStringConstant("speak");
    describeSuperBuilder.emitU16(OpCode::GetLocal, 0);     // self
    describeSuperBuilder.emitU16(OpCode::GetUpvalue, 0);   // super (== Animal)
    describeSuperBuilder.emitU16(OpCode::GetSuper, speakName2);
    describeSuperBuilder.emitU16(OpCode::Call, 0);
    describeSuperBuilder.emit(OpCode::Return);
    ObjFunction* describeSuperFn = describeSuperBuilder.build(0, "describeSuper");

    ChunkBuilder b(vm);
    uint16_t animalName = b.addStringConstant("Animal");
    uint16_t dogName = b.addStringConstant("Dog");
    uint16_t speakName = b.addStringConstant("speak");
    uint16_t describeName = b.addStringConstant("describe");
    uint16_t describeSuperName = b.addStringConstant("describeSuper");
    uint16_t animalSpeakIndex = b.addFunctionConstant(animalSpeakFn);
    uint16_t describeIndex = b.addFunctionConstant(describeFn);
    uint16_t dogSpeakIndex = b.addFunctionConstant(dogSpeakFn);
    uint16_t describeSuperIndex = b.addFunctionConstant(describeSuperFn);
    uint16_t dName = b.addStringConstant("d");
    uint16_t r1Name = b.addStringConstant("r1");
    uint16_t r2Name = b.addStringConstant("r2");
    uint16_t r3Name = b.addStringConstant("r3");

    // class Animal { speak() {...} describe() {...} }
    b.emitU16(OpCode::Class, animalName);
    b.emitU16(OpCode::DefineGlobal, animalName);
    b.emitU16(OpCode::GetGlobal, animalName);
    b.emitClosure(animalSpeakIndex, {});
    b.emitU16(OpCode::Method, speakName);
    b.emitClosure(describeIndex, {});
    b.emitU16(OpCode::Method, describeName);
    b.emit(OpCode::Pop);

    // class Dog extends Animal { speak() {...} describeSuper() {...} }
    // Dog is global, so the synthetic `super` local lives in the
    // top-level script's own frame (slot 0, since nothing else has taken
    // a local slot there yet) -- describeSuperFn captures it as upvalue 0
    // via FromEnclosingLocal=true, index=0.
    b.emitU16(OpCode::Class, dogName);
    b.emitU16(OpCode::DefineGlobal, dogName);
    b.emitU16(OpCode::GetGlobal, animalName);  // superclass
    b.emitU16(OpCode::GetGlobal, dogName);     // temp re-fetch
    b.emit(OpCode::Inherit);
    b.emitClosure(dogSpeakIndex, {});
    b.emitU16(OpCode::Method, speakName);
    b.emitClosure(describeSuperIndex, {{true, 0}});
    b.emitU16(OpCode::Method, describeSuperName);
    b.emit(OpCode::Pop);

    b.emitU16(OpCode::GetGlobal, dogName);
    b.emitU16(OpCode::Call, 0);
    b.emitU16(OpCode::DefineGlobal, dName);

    b.emitU16(OpCode::GetGlobal, dName);
    b.emitU16(OpCode::GetProperty, speakName);
    b.emitU16(OpCode::Call, 0);
    b.emitU16(OpCode::DefineGlobal, r1Name);

    b.emitU16(OpCode::GetGlobal, dName);
    b.emitU16(OpCode::GetProperty, describeName);
    b.emitU16(OpCode::Call, 0);
    b.emitU16(OpCode::DefineGlobal, r2Name);

    b.emitU16(OpCode::GetGlobal, dName);
    b.emitU16(OpCode::GetProperty, describeSuperName);
    b.emitU16(OpCode::Call, 0);
    b.emitU16(OpCode::DefineGlobal, r3Name);

    vm.interpret(b.build());

    REQUIRE(asString(*vm.getGlobal("r1")) == "Woof");
    REQUIRE(asString(*vm.getGlobal("r2")) == "Woof");
    REQUIRE(asString(*vm.getGlobal("r3")) == "...");
}

TEST_CASE("super.method() with an undefined method name is a runtime error", "[vm][classes]") {
    Vm vm;

    ChunkBuilder callSuperBuilder(vm);
    uint16_t missing = callSuperBuilder.addStringConstant("missing");
    callSuperBuilder.emitU16(OpCode::GetLocal, 0);
    callSuperBuilder.emitU16(OpCode::GetUpvalue, 0);
    callSuperBuilder.emitU16(OpCode::GetSuper, missing);
    ObjFunction* callSuperFn = callSuperBuilder.build(0, "callSuper");

    ChunkBuilder b(vm);
    uint16_t animalName = b.addStringConstant("Animal");
    uint16_t dogName = b.addStringConstant("Dog");
    uint16_t callSuperName = b.addStringConstant("callSuper");
    uint16_t callSuperIndex = b.addFunctionConstant(callSuperFn);
    uint16_t dName = b.addStringConstant("d");

    b.emitU16(OpCode::Class, animalName);
    b.emitU16(OpCode::DefineGlobal, animalName);

    b.emitU16(OpCode::Class, dogName);
    b.emitU16(OpCode::DefineGlobal, dogName);
    b.emitU16(OpCode::GetGlobal, animalName);
    b.emitU16(OpCode::GetGlobal, dogName);
    b.emit(OpCode::Inherit);
    b.emitClosure(callSuperIndex, {{true, 0}});
    b.emitU16(OpCode::Method, callSuperName);
    b.emit(OpCode::Pop);

    b.emitU16(OpCode::GetGlobal, dogName);
    b.emitU16(OpCode::Call, 0);
    b.emitU16(OpCode::DefineGlobal, dName);
    b.emitU16(OpCode::GetGlobal, dName);
    b.emitU16(OpCode::GetProperty, callSuperName);
    b.emitU16(OpCode::Call, 0);

    REQUIRE_THROWS_AS(vm.interpret(b.build()), RuntimeError);
}

TEST_CASE("aggressive stress-mode collection doesn't corrupt classes, instances, or bound methods", "[vm][classes][gc]") {
    Vm vm;
    vm.stressGc = true;

    ChunkBuilder getXBuilder(vm);
    uint16_t xName = getXBuilder.addStringConstant("x");
    getXBuilder.emitU16(OpCode::GetLocal, 0);
    getXBuilder.emitU16(OpCode::GetProperty, xName);
    getXBuilder.emit(OpCode::Return);
    ObjFunction* getXFn = getXBuilder.build(0, "getX");

    ChunkBuilder initBuilder(vm);
    uint16_t xName2 = initBuilder.addStringConstant("x");
    initBuilder.emitU16(OpCode::GetLocal, 0);
    initBuilder.emitU16(OpCode::GetLocal, 1);
    initBuilder.emitU16(OpCode::SetProperty, xName2);
    initBuilder.emit(OpCode::Pop);
    ObjFunction* initFn = initBuilder.build(1, "init");

    ChunkBuilder b(vm);
    uint16_t pointName = b.addStringConstant("Point");
    uint16_t initIndex = b.addFunctionConstant(initFn);
    uint16_t getXIndex = b.addFunctionConstant(getXFn);
    uint16_t initName = b.addStringConstant("init");
    uint16_t getXName = b.addStringConstant("getX");
    uint16_t fortyTwo = b.addNumberConstant(42);
    uint16_t pName = b.addStringConstant("p");
    uint16_t resultName = b.addStringConstant("result");

    b.emitU16(OpCode::Class, pointName);
    b.emitU16(OpCode::DefineGlobal, pointName);
    b.emitU16(OpCode::GetGlobal, pointName);
    b.emitClosure(initIndex, {});
    b.emitU16(OpCode::Method, initName);
    b.emitClosure(getXIndex, {});
    b.emitU16(OpCode::Method, getXName);
    b.emit(OpCode::Pop);

    b.emitU16(OpCode::GetGlobal, pointName);
    b.emitU16(OpCode::Constant, fortyTwo);
    b.emitU16(OpCode::Call, 1);
    b.emitU16(OpCode::DefineGlobal, pName);

    b.emitU16(OpCode::GetGlobal, pName);
    b.emitU16(OpCode::GetProperty, getXName);
    b.emitU16(OpCode::Call, 0);
    b.emitU16(OpCode::DefineGlobal, resultName);

    vm.interpret(b.build());

    REQUIRE(asNumber(*vm.getGlobal("result")) == 42.0);
}

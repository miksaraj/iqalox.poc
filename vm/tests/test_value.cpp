#include <catch2/catch_test_macros.hpp>

#include "object.hpp"
#include "value.hpp"
#include "vm.hpp"

using namespace iqalox;

TEST_CASE("stringify: nil, undef, and booleans", "[value]") {
    REQUIRE(stringify(NilValue) == "nil");
    REQUIRE(stringify(UndefValue) == "undef");
    REQUIRE(stringify(boolValue(true)) == "true");
    REQUIRE(stringify(boolValue(false)) == "false");
}

TEST_CASE("stringify: numbers strip a trailing .0, matching poc's stringify", "[value]") {
    REQUIRE(stringify(numberValue(1.0)) == "1");
    REQUIRE(stringify(numberValue(0.0)) == "0");
    REQUIRE(stringify(numberValue(-0.0)) == "-0");
    REQUIRE(stringify(numberValue(-5.5)) == "-5.5");
    REQUIRE(stringify(numberValue(3.14)) == "3.14");
}

TEST_CASE("stringify: fixed vs. scientific notation follows Python's threshold, not std::to_chars' own",
          "[value]") {
    // std::to_chars' own "general" format switches to scientific whenever
    // it's *shorter* (e.g. rendering 1e8 as "1e+08"), which disagrees with
    // Python/poc for plenty of ordinary values.
    REQUIRE(stringify(numberValue(100000000.0)) == "100000000");
    REQUIRE(stringify(numberValue(1e15)) == "1000000000000000");
    REQUIRE(stringify(numberValue(0.0001)) == "0.0001");

    // Python's actual switch points: exponent < -4 or >= 16.
    REQUIRE(stringify(numberValue(1e16)) == "1e+16");
    REQUIRE(stringify(numberValue(0.00001)) == "1e-05");
    REQUIRE(stringify(numberValue(1.5e300)) == "1.5e+300");
    REQUIRE(stringify(numberValue(1e-10)) == "1e-10");
}

TEST_CASE("stringify: a string prints unquoted", "[value]") {
    Vm vm;
    auto* s = vm.allocate<ObjString>("hello");
    REQUIRE(stringify(objValue(s)) == "hello");
}

TEST_CASE("stringify: a vector's own elements use repr rules, not stringify's", "[value]") {
    Vm vm;
    auto* vec = vm.allocate<ObjVector>();
    vec->elements = {numberValue(1.0), numberValue(2.5), objValue(vm.allocate<ObjString>("a"))};

    // Nested numbers keep their ".0" and nested strings are quoted, unlike
    // a bare top-level `stringify(1.0)` == "1" or `stringify("a")` == "a"
    // -- matching poc's vectors being plain Python lists, whose `str()`
    // renders each element with `repr()`, not `stringify()`.
    REQUIRE(stringify(objValue(vec)) == "[1.0, 2.5, 'a']");
}

TEST_CASE("stringify: nested vectors recurse", "[value]") {
    Vm vm;
    auto* inner1 = vm.allocate<ObjVector>();
    inner1->elements = {numberValue(1.0), numberValue(2.0)};
    auto* inner2 = vm.allocate<ObjVector>();
    inner2->elements = {numberValue(3.0), numberValue(4.0)};
    auto* outer = vm.allocate<ObjVector>();
    outer->elements = {objValue(inner1), objValue(inner2)};

    REQUIRE(stringify(objValue(outer)) == "[[1.0, 2.0], [3.0, 4.0]]");
}

TEST_CASE("stringify: a nested string containing a single quote reprs with double quotes", "[value]") {
    Vm vm;
    auto* vec = vm.allocate<ObjVector>();
    vec->elements = {objValue(vm.allocate<ObjString>("it's"))};

    REQUIRE(stringify(objValue(vec)) == "[\"it's\"]");
}

TEST_CASE("typeName reports a sensible name for each kind of value", "[value]") {
    Vm vm;
    REQUIRE(typeName(NilValue) == "nil");
    REQUIRE(typeName(UndefValue) == "undef");
    REQUIRE(typeName(boolValue(true)) == "bool");
    REQUIRE(typeName(numberValue(1.0)) == "number");
    REQUIRE(typeName(objValue(vm.allocate<ObjString>("s"))) == "string");
    REQUIRE(typeName(objValue(vm.allocate<ObjVector>())) == "vector");
}

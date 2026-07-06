import pytest

from conftest import run, get_var

from error import IqaloxRuntimeError


def test_function_call_returns_value():
    interpreter = run(
        "fun add(a, b) { return a + b; }\n"
        "var result = add 1, 2\n"
    )
    assert get_var(interpreter, "result") == 3.0


def test_function_without_return_yields_nil():
    interpreter = run(
        "fun noop() { var x = 1; }\n"
        "var result = noop()\n"
    )
    assert get_var(interpreter, "result") is None


def test_recursion():
    interpreter = run(
        "fun fact(n) { return (n == 1) ? n : (n * fact (n - 1)); }\n"
        "var result = fact 5\n"
    )
    assert get_var(interpreter, "result") == 120.0


def test_closures_capture_enclosing_scope():
    interpreter = run(
        "fun makeAdder(n) {\n"
        "    fun adder(i) { return n + i; }\n"
        "    return adder\n"
        "}\n"
        "var add5 = makeAdder 5\n"
        "var result = add5 10\n"
    )
    assert get_var(interpreter, "result") == 15.0


def test_closure_over_mutable_variable_persists_across_calls():
    interpreter = run(
        "fun createCounter() {\n"
        "    var c mut = 0\n"
        "    fun counter() { c = c + 1; return c; }\n"
        "    return counter\n"
        "}\n"
        "var count = createCounter()\n"
        "var first = count()\n"
        "var second = count()\n"
    )
    assert get_var(interpreter, "first") == 1.0
    assert get_var(interpreter, "second") == 2.0


def test_function_value_can_be_passed_without_being_called():
    interpreter = run(
        "fun makeFive() { return 5; }\n"
        "fun apply(func) { return func(); }\n"
        "var result = apply makeFive\n"
    )
    assert get_var(interpreter, "result") == 5.0


def test_calling_wrong_arity_raises():
    with pytest.raises(IqaloxRuntimeError):
        run("fun add(a, b) { return a + b; }\nadd 1\n")


def test_calling_a_non_callable_raises():
    with pytest.raises(IqaloxRuntimeError):
        run("var x = 1\nx 2\n")


def test_native_print_returns_nil(capsys):
    interpreter = run("var result = print 1\n")
    assert capsys.readouterr().out == "1\n"
    assert get_var(interpreter, "result") is None


def test_native_concat_joins_stringified_elements():
    interpreter = run('var result = concat ["a", 1, "b"]\n')
    assert get_var(interpreter, "result") == "a1b"


def test_native_concat_on_non_vector_raises_runtime_error():
    # Regression test: concat(5) used to crash with an uncaught Python
    # TypeError ('float' object is not iterable) instead of a clean
    # IqaloxRuntimeError (docs/PLAN-0.1-POC.md).
    with pytest.raises(IqaloxRuntimeError):
        run("var result = concat 5\n")


def test_print_can_be_shadowed_in_a_nested_scope():
    # print/concat are no longer reserved keywords -- just ordinary builtin
    # function values bound in the global environment, so a nested `var
    # print = ...` shadows them like any other name (a top-level redeclare
    # would hit the separate "already declared in this scope" rule instead).
    interpreter = run(
        "fun test() {\n"
        "    var print = 42\n"
        "    return print\n"
        "}\n"
        "var result = test()\n"
    )
    assert get_var(interpreter, "result") == 42.0


def test_parameters_are_immutable_by_default():
    with pytest.raises(IqaloxRuntimeError):
        run("fun f(a) { a = 2; }\nf 1\n")

import pytest

from conftest import run, get_var

from error import IqaloxRuntimeError


def test_modulo():
    interpreter = run("var result = 7 % 3\n")
    assert get_var(interpreter, "result") == 1.0


def test_power():
    interpreter = run("var result = 2 ^ 10\n")
    assert get_var(interpreter, "result") == 1024.0


def test_comparison_operators(capsys):
    run(
        "print (3 >= 3)\n"
        "print (3 <= 2)\n"
        "print (3 != 2)\n"
        "print (3 == 3)\n"
    )
    assert capsys.readouterr().out.splitlines() == ["true", "false", "true", "true"]


def test_logical_not():
    interpreter = run("var result = !false\n")
    assert get_var(interpreter, "result") is True

    interpreter = run("var result = !true\n")
    assert get_var(interpreter, "result") is False


def test_null_coalescing_falls_through_on_nil():
    interpreter = run("var result = nil ?? 5\n")
    assert get_var(interpreter, "result") == 5.0


def test_null_coalescing_keeps_non_nil_left_side():
    interpreter = run("var result = 1 ?? 5\n")
    assert get_var(interpreter, "result") == 1.0


def test_null_coalescing_short_circuits_the_right_side(capsys):
    # Regression test: `??` used to be wired through the generic Binary
    # node, which evaluates both operands unconditionally before even
    # looking at the operator -- so `sideEffect() ?? 5` ran `sideEffect()`
    # even when the left side wasn't nil (docs/PLAN-0.1-POC.md).
    run(
        "fun boom() { print \"boomed\"\nreturn 1; }\n"
        "var result = 5 ?? boom()\n"
    )
    assert capsys.readouterr().out == ""


def test_comma_operator_evaluates_both_sides_and_returns_the_right():
    # Regression test: the comma operator used to always evaluate to nil,
    # since Interpreter.visit_binary_expr had no TokenType.COMMA case at
    # all and silently fell through to its final `return None`.
    interpreter = run("var x mut = 0\nvar result = ((x = 1), 2)\n")
    assert get_var(interpreter, "result") == 2.0
    assert get_var(interpreter, "x") == 1.0


def test_elvis_operator_short_form():
    # `a ?: b` is sugar for `a ? a : b` -- the truthy left side is reused as
    # the "then" branch instead of being written twice.
    interpreter = run("var result = 1 ?: 2\n")
    assert get_var(interpreter, "result") == 1.0

    interpreter = run("var result = false ?: 2\n")
    assert get_var(interpreter, "result") == 2.0


def test_elvis_operator_evaluates_its_condition_only_once(capsys):
    # Regression test: the parser reuses the same expr object for both
    # Ternary.left (the condition) and Ternary.middle (the truthy value),
    # but visit_ternary_expr used to call self.evaluate() on each
    # independently -- double-evaluating a condition with a side effect
    # (docs/PLAN-0.1-POC.md).
    run(
        "fun sideEffect() { print \"ran\"\nreturn 1; }\n"
        "var result = sideEffect() ?: 2\n"
    )
    assert capsys.readouterr().out == "ran\n"


def test_division_by_zero_raises():
    with pytest.raises(IqaloxRuntimeError):
        run("var result = 1 / 0\n")


def test_arithmetic_on_non_numbers_raises():
    with pytest.raises(IqaloxRuntimeError):
        run('var result = "a" + 1\n')


def test_unary_minus_on_non_number_raises():
    with pytest.raises(IqaloxRuntimeError):
        run('var result = -"a"\n')

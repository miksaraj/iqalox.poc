import pytest

from conftest import parse, run, get_var

from error import IqaloxRuntimeError
from interpreter import Interpreter


def test_for_loop_runs_body_and_increments(capsys):
    run("for (var i mut = 1; i <= 3; ++i) { print i; }")
    assert capsys.readouterr().out.splitlines() == ["1", "2", "3"]


def test_for_loop_with_all_clauses_omitted():
    # `for (;;)` with the loop entirely driven by a ternary-branch `break`.
    interpreter = run(
        "var i mut = 0\n"
        "for (;;) {\n"
        "    (i == 3) ? break : (i = i + 1)\n"
        "}\n"
    )
    assert get_var(interpreter, "i") == 3.0


def test_continue_skips_rest_of_body():
    interpreter = run(
        "var total mut = 0\n"
        "for (var i mut = 0; i < 5; ++i) {\n"
        "    (i == 2) ? continue : (total = total + i)\n"
        "}\n"
    )
    assert get_var(interpreter, "total") == 1.0 + 3.0 + 4.0


def test_break_stops_the_loop_immediately():
    interpreter = run(
        "var total mut = 0\n"
        "for (var i mut = 0; i < 10; ++i) {\n"
        "    (i == 3) ? break : (total = total + i)\n"
        "}\n"
    )
    assert get_var(interpreter, "total") == 0.0 + 1.0 + 2.0


def test_prefix_increment_mutates_and_returns_new_value(capsys):
    run("var x mut = 1\nprint ++x\nprint x")
    assert capsys.readouterr().out.splitlines() == ["2", "2"]


def test_prefix_decrement_mutates():
    interpreter = run("var x mut = 5\n--x")
    assert get_var(interpreter, "x") == 4.0


def test_increment_on_immutable_variable_raises():
    interpreter = Interpreter()
    with pytest.raises(IqaloxRuntimeError):
        for stmt in parse("var x = 1\n++x"):
            interpreter.execute(stmt)


def test_logical_and_short_circuits():
    # If `and` didn't short-circuit, evaluating the right side would try to
    # increment an undeclared variable and blow up with a different error.
    interpreter = run("var result mut = false and undefined_name")
    assert get_var(interpreter, "result") is False


def test_logical_or_short_circuits():
    interpreter = run("var result mut = true or undefined_name")
    assert get_var(interpreter, "result") is True


def test_logical_and_returns_right_operand_when_left_truthy():
    interpreter = run("var result mut = 1 and 2")
    assert get_var(interpreter, "result") == 2.0


def test_false_and_nil_are_falsy(capsys):
    run("print false or nil\nprint false or 1")
    assert capsys.readouterr().out.splitlines() == ["nil", "1"]


def test_blank_lines_do_not_abort_interpretation(capsys):
    run("print 1\n\n\nprint 2")
    assert capsys.readouterr().out.splitlines() == ["1", "2"]

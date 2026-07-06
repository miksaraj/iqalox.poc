from conftest import run, get_var


def test_pipe_calls_the_function_with_the_left_side(capsys):
    run("fun square(n) { return n * n; }\n5 |> square |> print\n")
    assert capsys.readouterr().out == "25\n"


def test_pipe_into_native_function():
    interpreter = run('var result = ["a", "b"] |> concat\n')
    assert get_var(interpreter, "result") == "ab"


def test_pipe_left_side_can_be_a_full_expression():
    interpreter = run(
        "fun square(n) { return n * n; }\n"
        "var result = (1 + 2) |> square\n"
    )
    assert get_var(interpreter, "result") == 9.0


def test_ignore_evaluates_to_nil_with_no_side_effect(capsys):
    run(
        "var flag mut = true\n"
        "flag ? _ : print 1\n"
        "print 2\n"
    )
    # The `_` branch runs instead of `print 1` -- only "2" should print.
    assert capsys.readouterr().out == "2\n"


def test_ignore_branch_not_taken_still_runs_the_other_side(capsys):
    run(
        "var flag mut = false\n"
        "flag ? _ : print 1\n"
    )
    assert capsys.readouterr().out == "1\n"

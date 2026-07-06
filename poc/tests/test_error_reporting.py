import iqalox


def setup_function():
    # Iqalox's error/interpreter state is class-level (see iqalox.py's
    # module-duplication-avoidance comment), so tests that exercise it
    # through the real Iqalox entry point must reset it first -- otherwise
    # an error (or a defined variable) from one test would leak into the
    # next test's environment/assertions.
    iqalox.Iqalox.had_error = False
    iqalox.Iqalox.had_runtime_error = False
    iqalox.Iqalox.source_lines = []
    iqalox.Iqalox.interpreter = iqalox.Interpreter()


def test_parse_error_shows_source_line_and_caret(capsys):
    iqalox.Iqalox().run("var x = @\n")
    out = capsys.readouterr().out
    assert "var x = @" in out
    lines = out.splitlines()
    source_line_index = next(i for i, line in enumerate(lines) if "var x = @" in line)
    caret_line = lines[source_line_index + 1]
    assert caret_line.index("^") == lines[source_line_index].index("@")


def test_runtime_error_shows_source_line_and_caret(capsys):
    iqalox.Iqalox().run("var x = 1\nx = 2\n")
    out = capsys.readouterr().out
    assert "Assigning to immutable variable 'x' not allowed." in out
    assert "x = 2" in out
    assert "^" in out


def test_no_error_produces_no_source_context(capsys):
    iqalox.Iqalox().run("var x = 1\n")
    out = capsys.readouterr().out
    assert out == ""
    assert iqalox.Iqalox.had_error is False

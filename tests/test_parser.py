from conftest import parse

from expression import Literal, Logical, Binary, Break, Continue, Call, Grouping, Ignore, Ternary, Variable, Vector
from statement import For, Function, Return, Expression
from token import TokenType


def single_expr(source: str):
    stmts = parse(source)
    assert len(stmts) == 1
    assert isinstance(stmts[0], Expression)
    return stmts[0].expression


def test_true_false_nil_are_real_python_values():
    # Regression test: these used to be parsed as Literal(TokenType.TRUE)
    # etc. -- the TokenType enum member itself, not the value it stands for
    # -- which made `false` and `nil` evaluate as truthy.
    assert single_expr("false").value is False
    assert single_expr("true").value is True
    assert single_expr("nil").value is None


def test_logical_or_and_precedence():
    expr = single_expr("a and b or c and d")
    assert isinstance(expr, Logical)
    assert expr.operator.type == TokenType.OR
    assert isinstance(expr.left, Logical) and expr.left.operator.type == TokenType.AND
    assert isinstance(expr.right, Logical) and expr.right.operator.type == TokenType.AND


def test_null_coalescing_binds_looser_than_logical_and_tighter_than_ternary():
    # `??` sits between `logic_or` and `ternary`: logical operators group
    # first (`a ?? (b or c)`), and `??` itself groups before the ternary
    # gets to see it (`(a ?? b) ? c : d`).
    expr = single_expr("a ?? b or c")
    assert isinstance(expr, Binary) and expr.operator.type == TokenType.DOUBLE_QUESTION_MARK
    assert isinstance(expr.right, Logical) and expr.right.operator.type == TokenType.OR

    expr = single_expr("a ?? b ? c : d")
    assert isinstance(expr, Ternary)
    assert isinstance(expr.left, Binary) and expr.left.operator.type == TokenType.DOUBLE_QUESTION_MARK


def test_logical_sits_between_ternary_and_equality():
    # `a == b and c == d` should group as `(a == b) and (c == d)`, and the
    # whole thing should still be usable as a ternary condition without
    # needing its own wrapping parens.
    expr = single_expr("a == b and c == d ? 1 : 2")
    assert isinstance(expr, Ternary)
    assert isinstance(expr.left, Logical)


def test_break_and_continue_are_expressions():
    assert isinstance(single_expr("break"), Break)
    assert isinstance(single_expr("continue"), Continue)


def test_break_continue_usable_as_ternary_branches():
    expr = single_expr("a ? continue : b ? break : c")
    assert isinstance(expr, Ternary)
    assert isinstance(expr.middle, Continue)
    assert isinstance(expr.right, Ternary)
    assert isinstance(expr.right.middle, Break)


def test_for_statement_parses_all_clauses():
    stmts = parse("for (var i mut = 0; i < 5; ++i) { print i; }")
    assert len(stmts) == 1
    for_stmt = stmts[0]
    assert isinstance(for_stmt, For)
    assert for_stmt.initializer is not None
    assert for_stmt.condition is not None
    assert for_stmt.increment is not None
    assert for_stmt.body is not None


def test_for_statement_clauses_are_optional():
    stmts = parse("for (;;) { break; }")
    for_stmt = stmts[0]
    assert for_stmt.initializer is None
    assert for_stmt.condition is None
    assert for_stmt.increment is None


def test_increment_requires_assignable_target():
    # `++5` isn't a variable, so it shouldn't parse as an increment.
    stmts = parse("++5")
    assert stmts == []


def test_increment_on_variable_parses():
    expr = single_expr("++x")
    assert expr.operator.type == TokenType.PLUS_PLUS
    assert isinstance(expr.right, Variable)


def test_mut_declaration_parses():
    stmts = parse("var x mut = 1")
    assert stmts[0].is_mutable is True


def test_immutable_declaration_parses():
    stmts = parse("var x = 1")
    assert stmts[0].is_mutable is False


def test_blank_lines_between_statements_do_not_error():
    stmts = parse("var x = 1\n\n\nprint x")
    assert len(stmts) == 2


def test_function_declaration_parses_params_and_body():
    stmts = parse("fun add(a, b) { return a + b; }")
    assert len(stmts) == 1
    fn = stmts[0]
    assert isinstance(fn, Function)
    assert fn.name.lexeme == "add"
    assert [p.lexeme for p in fn.params] == ["a", "b"]
    assert len(fn.body) == 1
    assert isinstance(fn.body[0], Return)


def test_zero_arg_call_requires_empty_parens():
    call = single_expr("f()")
    assert isinstance(call, Call)
    assert call.callee.name.lexeme == "f"
    assert call.arguments == []


def test_bare_identifier_is_not_a_call():
    # No `()` and nothing recognizable as an argument follows -- `f` alone
    # is a value reference, not an invocation (needed so functions can be
    # passed around without being called, e.g. `return adder`).
    expr = single_expr("f")
    assert isinstance(expr, Variable)


def test_single_arg_call_needs_no_parens():
    call = single_expr("f 1")
    assert isinstance(call, Call)
    assert len(call.arguments) == 1
    assert isinstance(call.arguments[0], Literal) and call.arguments[0].value == 1.0


def test_multi_arg_call_is_comma_separated_without_wrapping_parens():
    call = single_expr("f 1, 2, 3")
    assert isinstance(call, Call)
    assert [a.value for a in call.arguments] == [1.0, 2.0, 3.0]


def test_compound_argument_needs_grouping_parens():
    # `f - 1` is subtraction (f minus 1), never a call to f with a bare
    # unary/binary expression as its argument.
    expr = single_expr("f - 1")
    assert isinstance(expr, Binary) and expr.operator.type == TokenType.MINUS

    call = single_expr("f (n - 1)")
    assert isinstance(call, Call)
    assert len(call.arguments) == 1
    assert isinstance(call.arguments[0], Grouping)


def test_nested_call_needs_no_extra_parens():
    # `concat` immediately followed by a vector literal is itself a complete
    # call, which then becomes print's one argument.
    call = single_expr("print concat [1, 2]")
    assert isinstance(call, Call) and call.callee.name.lexeme == "print"
    assert len(call.arguments) == 1
    inner = call.arguments[0]
    assert isinstance(inner, Call) and inner.callee.name.lexeme == "concat"
    assert isinstance(inner.arguments[0], Vector)


def test_vector_literal_with_multiple_elements():
    # Regression test: `comma()`'s handling of `comma_as_operator = False`
    # used to discard the first element and choke on a 3rd+ element.
    vector = single_expr("[1, 2, 3]")
    assert isinstance(vector, Vector)
    assert [v.value for v in vector.values] == [1.0, 2.0, 3.0]


def test_empty_and_single_element_vector_literals():
    assert single_expr("[]").values == []
    assert [v.value for v in single_expr("[1]").values] == [1.0]


def test_pipe_desugars_to_a_call():
    call = single_expr("a |> f")
    assert isinstance(call, Call)
    assert call.callee.name.lexeme == "f"
    assert isinstance(call.arguments[0], Variable) and call.arguments[0].name.lexeme == "a"


def test_pipe_chains_left_associatively():
    # `a |> f |> g` is `g(f(a))`.
    call = single_expr("a |> f |> g")
    assert isinstance(call, Call) and call.callee.name.lexeme == "g"
    inner = call.arguments[0]
    assert isinstance(inner, Call) and inner.callee.name.lexeme == "f"
    assert isinstance(inner.arguments[0], Variable) and inner.arguments[0].name.lexeme == "a"


def test_pipe_requires_a_function_reference_on_the_right():
    # `a |> 1` doesn't make sense -- the right side must be a bare name.
    stmts = parse("a |> 1")
    assert stmts == []


def test_pipe_binds_looser_than_comma_and_ternary():
    # A ternary as the whole pipe input needs no extra grouping.
    call = single_expr("(a ? b : c) |> f")
    assert isinstance(call, Call)
    assert isinstance(call.arguments[0], Grouping)
    assert isinstance(call.arguments[0].expression, Ternary)


def test_ignore_operator_is_an_expression():
    assert isinstance(single_expr("_"), Ignore)


def test_ignore_operator_usable_as_ternary_branch():
    expr = single_expr("a ? _ : b")
    assert isinstance(expr, Ternary)
    assert isinstance(expr.middle, Ignore)

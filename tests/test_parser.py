from conftest import parse

from expression import Literal, Logical, Binary, Break, Continue, Ternary, Variable
from statement import For, Expression
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

from token import Token, TokenType
from expression import Expr, Binary, Grouping, Literal, Unary, ExprVisitor


class AstPrinter(ExprVisitor):
    def print(self, expr: Expr) -> str:
        return expr.accept(self)

    def parenthesize(self, name: str, *exprs: Expr) -> str:
        content = ' '.join(expr.accept(self) for expr in exprs)
        return f'({name} {content})'

    def visit_binary_expr(self, expr: Binary) -> str:
        return self.parenthesize(expr.operator.lexeme, expr.left, expr.right)

    def visit_grouping_expr(self, expr: Grouping) -> str:
        return self.parenthesize('group', expr.expression)

    def visit_literal_expr(self, expr: Literal) -> str:
        if expr.value is None:
            return 'nil'
        return str(expr.value)

    def visit_unary_expr(self, expr: Unary) -> str:
        return self.parenthesize(expr.operator.lexeme, expr.right)

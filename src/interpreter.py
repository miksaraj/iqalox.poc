from typing import Any

from expression import Expr, ExprVisitor, Binary, Unary, Literal, Grouping, Ternary
from token import Token, TokenType


class IqaloxRuntimeError(RuntimeError):
    def __init__(self, token: Token, message: str):
        super().__init__(message)
        self.token = token

    def __str__(self) -> str:
        return super().__str__()

    def __repr__(self) -> str:
        return super().__repr__()


class Interpreter(ExprVisitor):
    def interpret(self, expr: Expr) -> None:
        try:
            value = self.evaluate(expr)
            print(self.stringify(value))
        except IqaloxRuntimeError:
            return

    @staticmethod
    def stringify(obj: Any) -> str:
        if obj is None:
            return 'nil'
        if isinstance(obj, float):
            text = str(obj)
            if text.endswith('.0'):
                text = text[:-2]
            return text
        if isinstance(obj, str):
            return obj
        if isinstance(obj, bool):
            return str(obj).lower()
        return str(obj)

    def evaluate(self, expr: Expr) -> Any:
        return expr.accept(self)

    def is_truthy(self, obj: Any) -> bool:
        if obj is None:
            return False
        if isinstance(obj, bool):
            return obj
        return True

    @staticmethod
    def check_number_operand(operator: Token, operand: Any) -> None:
        if isinstance(operand, (int, float)):
            return
        raise IqaloxRuntimeError(operator, 'Operand must be a number.')

    @staticmethod
    def check_number_operands(operator: Token, left: Any, right: Any) -> None:
        if isinstance(left, (int, float)) and isinstance(right, (int, float)):
            return
        raise IqaloxRuntimeError(operator, 'Operands must be numbers.')

    @staticmethod
    def is_equal(a: Any, b: Any) -> bool:
        return a == b

    def visit_literal_expr(self, expr: Literal) -> Any:
        return expr.value

    def visit_grouping_expr(self, expr: Grouping) -> Any:
        return self.evaluate(expr.expression)

    def visit_unary_expr(self, expr: Unary) -> Any:
        right = self.evaluate(expr.right)

        if expr.operator.type == TokenType.BANG:
            return not self.is_truthy(right)
        elif expr.operator.type == TokenType.MINUS:
            self.check_number_operand(expr.operator, right)
            return -float(right)

        return None

    def visit_binary_expr(self, expr: Binary) -> Any:
        left = self.evaluate(expr.left)
        right = self.evaluate(expr.right)

        token_type = expr.operator.type

        if token_type == TokenType.BANG_EQUAL:
            return not self.is_equal(left, right)
        elif token_type == TokenType.EQUAL_EQUAL:
            return self.is_equal(left, right)
        elif token_type == TokenType.GREATER:
            self.check_number_operands(expr.operator, left, right)
            return left > right
        elif token_type == TokenType.GREATER_EQUAL:
            self.check_number_operands(expr.operator, left, right)
            return left >= right
        elif token_type == TokenType.LESS:
            self.check_number_operands(expr.operator, left, right)
            return left < right
        elif token_type == TokenType.LESS_EQUAL:
            self.check_number_operands(expr.operator, left, right)
            return left <= right
        elif token_type == TokenType.MINUS:
            self.check_number_operands(expr.operator, left, right)
            return left - right
        elif token_type == TokenType.PLUS:
            self.check_number_operands(expr.operator, left, right)
            return left + right
        elif token_type == TokenType.SLASH:
            self.check_number_operands(expr.operator, left, right)
            if right == 0:
                raise IqaloxRuntimeError(expr.operator, 'Division by zero.')
            return left / right
        elif token_type == TokenType.STAR:
            self.check_number_operands(expr.operator, left, right)
            return left * right
        elif token_type == TokenType.PERCENT:
            self.check_number_operands(expr.operator, left, right)
            return left % right
        elif token_type == TokenType.POWER:
            self.check_number_operands(expr.operator, left, right)
            return left ** right
        elif token_type == TokenType.DOUBLE_QUESTION_MARK:
            return right if left is None else left

        return None

    def visit_ternary_expr(self, expr: Ternary) -> Any:
        left = self.evaluate(expr.left)

        if self.is_truthy(left):
            return self.evaluate(expr.middle)
        else:
            return self.evaluate(expr.right)

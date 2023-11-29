from typing import Any, List

from expression import Expr, ExprVisitor, Binary, Unary, Literal, Grouping, Ternary, Vector, Variable, Assign
from statement import Stmt, StmtVisitor, Expression, Print, Concat, Var, Block
from token import Token, TokenType
from error import IqaloxRuntimeError
from environment import Environment, VariableData


class Interpreter(ExprVisitor, StmtVisitor):
    def __init__(self) -> None:
        self.environment = Environment()

    def execute(self, stmt: Stmt) -> None:
        stmt.accept(self)

    def execute_block(self, statements: List[Stmt], environment: Environment) -> None:
        previous = self.environment
        try:
            self.environment = environment
            for statement in statements:
                self.execute(statement)
        finally:
            self.environment = previous

    def interpret(self, statements: List[Stmt]) -> None:
        try:
            for statement in statements:
                self.execute(statement)
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

    @staticmethod
    def is_truthy(obj: Any) -> bool:
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

    def visit_block_stmt(self, stmt: Block) -> None:
        self.execute_block(stmt.statements, Environment(self.environment))
        return None

    def visit_expression_stmt(self, stmt: Expression) -> None:
        self.evaluate(stmt.expression)
        return None

    def visit_print_stmt(self, stmt: Print) -> None:
        value = self.evaluate(stmt.expression)
        print(self.stringify(value))
        return None

    def visit_concat_stmt(self, stmt: Concat) -> Any:
        return ''.join(
            [self.stringify(value) for value in self.evaluate(stmt.expression)]
        )

    def visit_var_stmt(self, stmt: Var) -> None:
        value = None
        if stmt.initializer is not None:
            value = self.evaluate(stmt.initializer)

        self.environment.define(stmt.name.lexeme, VariableData(value, stmt.is_mutable))
        return None

    def visit_assign_expr(self, expr: Assign) -> Any:
        value = self.evaluate(expr.value)
        self.environment.assign(expr.name, value)
        return value

    def visit_literal_expr(self, expr: Literal) -> Any:
        return expr.value

    def visit_grouping_expr(self, expr: Grouping) -> Any:
        return self.evaluate(expr.expression)

    def visit_vector_expr(self, expr: Vector) -> Any:
        return [self.evaluate(value) for value in expr.values]

    def visit_unary_expr(self, expr: Unary) -> Any:
        right = self.evaluate(expr.right)

        if expr.operator.type == TokenType.PLUS_PLUS:
            self.check_number_operand(expr.operator, right)
            return float(right) + 1
        elif expr.operator.type == TokenType.MINUS_MINUS:
            self.check_number_operand(expr.operator, right)
            return float(right) - 1
        elif expr.operator.type == TokenType.BANG:
            return not self.is_truthy(right)
        elif expr.operator.type == TokenType.MINUS:
            self.check_number_operand(expr.operator, right)
            return -float(right)

        return None

    def visit_variable_expr(self, expr: Variable) -> Any:
        return self.environment.get(expr.name)

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

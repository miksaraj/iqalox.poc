from abc import ABC, abstractmethod
from typing import Any, List
from token import Token


class ExprVisitor(ABC):
    @abstractmethod
    def visit_assign_expr(self, expr: 'Expr') -> Any:
        pass

    @abstractmethod
    def visit_binary_expr(self, expr: 'Expr') -> Any:
        pass

    @abstractmethod
    def visit_grouping_expr(self, expr: 'Expr') -> Any:
        pass

    @abstractmethod
    def visit_literal_expr(self, expr: 'Expr') -> Any:
        pass

    @abstractmethod
    def visit_unary_expr(self, expr: 'Expr') -> Any:
        pass

    @abstractmethod
    def visit_ternary_expr(self, expr: 'Expr') -> Any:
        pass

    @abstractmethod
    def visit_vector_expr(self, expr: 'Expr') -> Any:
        pass

    @abstractmethod
    def visit_variable_expr(self, expr: 'Expr') -> Any:
        pass


class Expr(ABC):
    @abstractmethod
    def accept(self, visitor: ExprVisitor) -> Any:
        pass


class Assign(Expr):
    def __init__(self, name: Token, value: Expr) -> None:
        self.name = name
        self.value = value

    def accept(self, visitor: ExprVisitor) -> None:
        return visitor.visit_assign_expr(self)


class Binary(Expr):
    def __init__(self, left: Expr, operator: Token, right: Expr) -> None:
        self.left = left
        self.operator = operator
        self.right = right

    def accept(self, visitor: ExprVisitor) -> None:
        return visitor.visit_binary_expr(self)


class Grouping(Expr):
    def __init__(self, expression: Expr) -> None:
        self.expression = expression

    def accept(self, visitor: ExprVisitor) -> None:
        return visitor.visit_grouping_expr(self)


class Literal(Expr):
    def __init__(self, value: Any) -> None:
        self.value = value

    def accept(self, visitor: ExprVisitor) -> None:
        return visitor.visit_literal_expr(self)


class Unary(Expr):
    def __init__(self, operator: Token, right: Expr) -> None:
        self.operator = operator
        self.right = right

    def accept(self, visitor: ExprVisitor) -> None:
        return visitor.visit_unary_expr(self)


class Ternary(Expr):
    def __init__(self, left: Expr, left_operator: Token, middle: Expr, right_operator: Token, right: Expr) -> None:
        self.left = left
        self.left_operator = left_operator
        self.middle = middle
        self.right_operator = right_operator
        self.right = right

    def accept(self, visitor: ExprVisitor) -> None:
        return visitor.visit_ternary_expr(self)


class Vector(Expr):
    def __init__(self, values: List[Expr]) -> None:
        self.values = values

    def accept(self, visitor: ExprVisitor) -> None:
        return visitor.visit_vector_expr(self)


class Variable(Expr):
    def __init__(self, name: Token) -> None:
        self.name = name

    def accept(self, visitor: ExprVisitor) -> None:
        return visitor.visit_variable_expr(self)


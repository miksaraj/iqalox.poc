from abc import ABC, abstractmethod
from typing import Any, List
from expression import Expr
from token import Token


class StmtVisitor(ABC):
    @abstractmethod
    def visit_block_stmt(self, expr: 'Stmt') -> Any:
        pass

    @abstractmethod
    def visit_expression_stmt(self, expr: 'Stmt') -> Any:
        pass

    @abstractmethod
    def visit_print_stmt(self, expr: 'Stmt') -> Any:
        pass

    @abstractmethod
    def visit_concat_stmt(self, expr: 'Stmt') -> Any:
        pass

    @abstractmethod
    def visit_var_stmt(self, expr: 'Stmt') -> Any:
        pass


class Stmt(ABC):
    @abstractmethod
    def accept(self, visitor: StmtVisitor) -> Any:
        pass


class Block(Stmt):
    def __init__(self, statements: List[Stmt]) -> None:
        self.statements = statements

    def accept(self, visitor: StmtVisitor) -> None:
        return visitor.visit_block_stmt(self)


class Expression(Stmt):
    def __init__(self, expression: Expr) -> None:
        self.expression = expression

    def accept(self, visitor: StmtVisitor) -> None:
        return visitor.visit_expression_stmt(self)


class Print(Stmt):
    def __init__(self, expression: Expr) -> None:
        self.expression = expression

    def accept(self, visitor: StmtVisitor) -> None:
        return visitor.visit_print_stmt(self)


class Concat(Stmt):
    def __init__(self, expression: Expr) -> None:
        self.expression = expression

    def accept(self, visitor: StmtVisitor) -> None:
        return visitor.visit_concat_stmt(self)


class Var(Stmt):
    def __init__(self, name: Token, initializer: Expr, is_mutable: bool) -> None:
        self.name = name
        self.initializer = initializer
        self.is_mutable = is_mutable

    def accept(self, visitor: StmtVisitor) -> None:
        return visitor.visit_var_stmt(self)


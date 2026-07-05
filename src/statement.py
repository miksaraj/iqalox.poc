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
    def visit_var_stmt(self, expr: 'Stmt') -> Any:
        pass

    @abstractmethod
    def visit_for_stmt(self, expr: 'Stmt') -> Any:
        pass

    @abstractmethod
    def visit_function_stmt(self, expr: 'Stmt') -> Any:
        pass

    @abstractmethod
    def visit_return_stmt(self, expr: 'Stmt') -> Any:
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


class Var(Stmt):
    def __init__(self, name: Token, initializer: Expr, is_mutable: bool) -> None:
        self.name = name
        self.initializer = initializer
        self.is_mutable = is_mutable

    def accept(self, visitor: StmtVisitor) -> None:
        return visitor.visit_var_stmt(self)


class For(Stmt):
    def __init__(self, initializer: Stmt, condition: Expr, increment: Expr, body: Stmt) -> None:
        self.initializer = initializer
        self.condition = condition
        self.increment = increment
        self.body = body

    def accept(self, visitor: StmtVisitor) -> None:
        return visitor.visit_for_stmt(self)


class Function(Stmt):
    def __init__(self, name: Token, params: List[Token], body: List[Stmt]) -> None:
        self.name = name
        self.params = params
        self.body = body

    def accept(self, visitor: StmtVisitor) -> None:
        return visitor.visit_function_stmt(self)


class Return(Stmt):
    def __init__(self, keyword: Token, value: Expr) -> None:
        self.keyword = keyword
        self.value = value

    def accept(self, visitor: StmtVisitor) -> None:
        return visitor.visit_return_stmt(self)


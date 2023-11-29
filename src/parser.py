from typing import List, Optional

from expression import Expr, Binary, Unary, Literal, Grouping, Ternary, Vector, Variable, Assign
from statement import Stmt, Expression, Print, Concat, Var, Block
from token import Token, TokenType
from error import ParseError


class Parser:
    def __init__(self, tokens: List[Token]) -> None:
        self.tokens = tokens
        self.current = 0
        self.comma_as_operator = True

    def parse(self) -> List[Stmt]:
        statements: List[Stmt] = []

        while not self.is_at_end():
            statements.append(self.declaration())
        return statements

    def expression(self) -> Optional[Expr]:
        return self.assignment()

    def declaration(self) -> Optional[Stmt]:
        try:
            if self.match(TokenType.VAR):
                return self.var_declaration()
            return self.statement()
        except ParseError:
            self.synchronize()
            return None

    def statement(self) -> Optional[Stmt]:
        if self.match(TokenType.PRINT):
            return self.print_statement()
        if self.match(TokenType.CONCAT):
            return self.concat_statement()
        if self.match(TokenType.LEFT_BRACE):
            return Block(self.block())
        return self.expression_statement()

    def print_statement(self) -> Optional[Stmt]:
        value = self.expression()
        self.consume(TokenType.SEMICOLON, "Expect line break or ';' after value.")
        return Print(value)

    def concat_statement(self) -> Optional[Stmt]:
        expr = self.expression()
        self.consume(TokenType.SEMICOLON, "Expect line break or ';' after value.")
        return Concat(expr)

    def var_declaration(self) -> Optional[Stmt]:
        name = self.consume(TokenType.IDENTIFIER, "Expect variable name.")

        is_mutable = False
        if self.match(TokenType.MUTABLE):
            is_mutable = True
            self.advance()

        initializer = None
        if self.match(TokenType.EQUAL):
            initializer = self.expression()
        elif not is_mutable:
            raise self.error(self.peek(), "Expect '=' after 'var'. Immutable variables must be initialized.")

        self.consume(TokenType.SEMICOLON, "Expect line break or ';' after variable declaration.")
        return Var(name, initializer, is_mutable)

    def expression_statement(self) -> Optional[Stmt]:
        expr = self.expression()
        self.consume(TokenType.SEMICOLON, "Expect line break or ';' after expression.")
        return Expression(expr)

    def block(self) -> List[Stmt]:
        statements: List[Stmt] = []

        while not self.check(TokenType.RIGHT_BRACE) and not self.is_at_end():
            statements.append(self.declaration())

        self.consume(TokenType.RIGHT_BRACE, "Expect '}' after block.")
        return statements

    def assignment(self) -> Optional[Expr]:
        expr = self.comma()

        if self.match(TokenType.EQUAL):
            equals = self.previous()
            value = self.assignment()

            if isinstance(expr, Variable):
                name = expr.name
                return Assign(name, value)

            raise self.error(equals, "Invalid assignment target.")

        return expr

    def comma(self) -> Optional[Expr]:
        expr = self.ternary()

        if not self.comma_as_operator:
            self.advance()
            expr = self.ternary()

        while self.comma_as_operator and self.match(TokenType.COMMA):
            operator = self.previous()
            right = self.ternary()
            expr = Binary(expr, operator, right)

        return expr

    def ternary(self) -> Optional[Expr]:
        expr = self.equality()

        if self.match(TokenType.QUESTION_MARK):
            left_operator = self.previous()
            middle = self.expression()
            right_operator = self.consume(TokenType.COLON, "Expect ':' in ternary operator.")
            right = self.expression()
            expr = Ternary(expr, left_operator, middle, right_operator, right)
        elif self.match(TokenType.QUESTION_MARK_COLON):
            left_operator = self.previous()
            right_operator = self.previous()
            right = self.expression()
            expr = Ternary(expr, left_operator, expr, right_operator, right)

        return expr

    def equality(self) -> Optional[Expr]:
        expr = self.comparison()

        while self.match(TokenType.BANG_EQUAL, TokenType.EQUAL_EQUAL):
            operator = self.previous()
            right = self.comparison()
            expr = Binary(expr, operator, right)

        return expr

    def comparison(self) -> Optional[Expr]:
        expr = self.addition()

        while self.match(TokenType.GREATER, TokenType.GREATER_EQUAL, TokenType.LESS, TokenType.LESS_EQUAL):
            operator = self.previous()
            right = self.addition()
            expr = Binary(expr, operator, right)

        return expr

    def addition(self) -> Optional[Expr]:
        expr = self.multiplication()

        while self.match(TokenType.MINUS, TokenType.PLUS):
            operator = self.previous()
            right = self.multiplication()
            expr = Binary(expr, operator, right)

        return expr

    def multiplication(self) -> Optional[Expr]:
        expr = self.increment()

        while self.match(
                TokenType.SLASH,
                TokenType.STAR,
                TokenType.PERCENT,
                TokenType.POWER,
                TokenType.DOUBLE_QUESTION_MARK
        ):
            operator = self.previous()
            right = self.increment()
            expr = Binary(expr, operator, right)

        return expr

    def increment(self) -> Optional[Expr]:
        if self.match(TokenType.PLUS_PLUS, TokenType.MINUS_MINUS):
            operator = self.previous()
            right = self.unary()
            return Unary(operator, right)

        return self.unary()

    def unary(self) -> Optional[Expr]:
        if self.match(TokenType.BANG, TokenType.MINUS):
            operator = self.previous()
            right = self.unary()
            return Unary(operator, right)

        return self.primary()

    def primary(self) -> Optional[Expr]:
        if self.match(TokenType.FALSE):
            return Literal(TokenType.FALSE)
        if self.match(TokenType.TRUE):
            return Literal(TokenType.TRUE)
        if self.match(TokenType.NIL):
            return Literal(TokenType.NIL)

        if self.match(TokenType.NUMBER, TokenType.STRING):
            return Literal(self.previous().literal)

        if self.match(TokenType.IDENTIFIER):
            return Variable(self.previous())

        if self.match(TokenType.LEFT_PAREN):
            expr = self.expression()
            self.consume(TokenType.RIGHT_PAREN, "Expect ')' after expression.")
            return Grouping(expr)

        if self.match(TokenType.LEFT_BRACKET):
            exprs = []
            self.comma_as_operator = False

            while not self.match(TokenType.RIGHT_BRACKET):
                exprs.append(self.expression())

            self.consume(TokenType.RIGHT_BRACKET, "Expect ']' after expression.")
            self.comma_as_operator = True
            return Vector(exprs)

        raise self.error(self.peek(), "Expect expression.")

    def match(self, *types: TokenType) -> bool:
        for _type in types:
            if self.check(_type):
                self.advance()
                return True

        return False

    def consume(self, _type: TokenType, message: str) -> Token:
        if self.check(_type):
            return self.advance()

        raise self.error(self.peek(), message)

    def check(self, _type: TokenType) -> bool:
        if self.is_at_end():
            return False
        return self.peek().type == _type

    def advance(self) -> Token:
        if not self.is_at_end():
            self.current += 1
        return self.previous()

    def is_at_end(self) -> bool:
        return self.peek().type == TokenType.EOF

    def peek(self) -> Token:
        return self.tokens[self.current]

    def previous(self) -> Optional[Token]:
        return self.tokens[self.current - 1]

    @staticmethod
    def error(token: Token, message: str) -> ParseError:
        import iqalox
        iqalox.Iqalox.error(token, message)
        return ParseError(token, message)

    def synchronize(self) -> None:
        self.advance()

        while not self.is_at_end():
            if self.previous().type == TokenType.SEMICOLON:
                return

            if self.peek().type in [
                TokenType.CLASS,
                TokenType.FUN,
                TokenType.VAR,
                TokenType.FOR,
                TokenType.PRINT,
                TokenType.RETURN
            ]:
                return

            self.advance()

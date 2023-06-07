from typing import List, Optional

from expression import Expr, Binary, Unary, Literal, Grouping, Ternary
from token import Token, TokenType


class Parser:
    def __init__(self, tokens: List[Token]) -> None:
        self.tokens = tokens
        self.current = 0

    class ParseError(RuntimeError):
        def __init__(self, token: Token, message: str):
            super().__init__(message)
            self.token = token

    def parse(self) -> Optional[Expr]:
        try:
            return self.expression()
        except self.ParseError:
            return None

    def expression(self) -> Optional[Expr]:
        return self.comma()

    def comma(self) -> Optional[Expr]:
        expr = self.ternary()

        while self.match(TokenType.COMMA):
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

        if self.match(TokenType.LEFT_PAREN):
            expr = self.expression()
            self.consume(TokenType.RIGHT_PAREN, "Expect ')' after expression.")
            return Grouping(expr)

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
        return Parser.ParseError(token, message)

    def synchronize(self) -> None:
        self.advance()

        while not self.is_at_end():
            if self.previous().type == TokenType.SEMICOLON:  # This is for the benefit of REPL usage only
                return
            if self.previous().line < self.peek().line:
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

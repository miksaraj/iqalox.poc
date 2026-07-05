from typing import List, Optional

from expression import Expr, Binary, Logical, Unary, Literal, Grouping, Ternary, Vector, Variable, Assign, Break, \
    Continue, Call, Ignore
from statement import Stmt, Expression, Var, Block, For, Function, Return
from token import Token, TokenType
from error import ParseError

# Deliberately excludes operator tokens like MINUS: `f - 1` is subtraction,
# never a call to `f` with argument `-1` (that needs `f (-1)`) -- this is
# what keeps `x - 1` and `f 1` unambiguous without lookahead past one token.
ARGUMENT_START_TOKENS = (
    TokenType.LEFT_PAREN, TokenType.LEFT_BRACKET, TokenType.FALSE, TokenType.TRUE, TokenType.NIL,
    TokenType.NUMBER, TokenType.STRING, TokenType.IDENTIFIER, TokenType.BREAK, TokenType.CONTINUE,
    TokenType.UNDERSCORE,
)


class Parser:
    def __init__(self, tokens: List[Token]) -> None:
        self.tokens = tokens
        self.current = 0
        self.comma_as_operator = True

    def parse(self) -> List[Stmt]:
        statements: List[Stmt] = []

        while not self.is_at_end():
            stmt = self.declaration()
            if stmt is not None:
                statements.append(stmt)
        return statements

    def expression(self) -> Optional[Expr]:
        return self.assignment()

    def declaration(self) -> Optional[Stmt]:
        try:
            if self.match(TokenType.FUN):
                return self.function_declaration('function')
            if self.match(TokenType.VAR):
                return self.var_declaration()
            return self.statement()
        except ParseError:
            self.synchronize()
            return None

    def function_declaration(self, kind: str) -> Stmt:
        name = self.consume(TokenType.IDENTIFIER, f"Expect {kind} name.")
        self.consume(TokenType.LEFT_PAREN, f"Expect '(' after {kind} name.")

        parameters: List[Token] = []
        if not self.check(TokenType.RIGHT_PAREN):
            while True:
                parameters.append(self.consume(TokenType.IDENTIFIER, "Expect parameter name."))
                if not self.match(TokenType.COMMA):
                    break

        self.consume(TokenType.RIGHT_PAREN, "Expect ')' after parameters.")
        self.consume(TokenType.LEFT_BRACE, f"Expect '{{' before {kind} body.")
        body = self.block()

        return Function(name, parameters, body)

    def statement(self) -> Optional[Stmt]:
        if self.match(TokenType.SEMICOLON):
            return None
        if self.match(TokenType.FOR):
            return self.for_statement()
        if self.match(TokenType.RETURN):
            return self.return_statement()
        if self.match(TokenType.LEFT_BRACE):
            return Block(self.block())
        return self.expression_statement()

    def return_statement(self) -> Stmt:
        keyword = self.previous()

        value = None
        if not self.check(TokenType.SEMICOLON):
            value = self.expression()

        self.consume(TokenType.SEMICOLON, "Expect line break or ';' after return value.")
        return Return(keyword, value)

    def for_statement(self) -> Optional[Stmt]:
        self.consume(TokenType.LEFT_PAREN, "Expect '(' after 'for'.")

        if self.match(TokenType.SEMICOLON):
            initializer = None
        elif self.match(TokenType.VAR):
            initializer = self.var_declaration()
        else:
            initializer = self.expression_statement()

        condition = None
        if not self.check(TokenType.SEMICOLON):
            condition = self.expression()
        self.consume(TokenType.SEMICOLON, "Expect ';' after loop condition.")

        increment = None
        if not self.check(TokenType.RIGHT_PAREN):
            increment = self.expression()
        self.consume(TokenType.RIGHT_PAREN, "Expect ')' after for clauses.")

        body = self.statement()

        return For(initializer, condition, increment, body)

    def var_declaration(self) -> Optional[Stmt]:
        name = self.consume(TokenType.IDENTIFIER, "Expect variable name.")

        is_mutable = self.match(TokenType.MUTABLE)

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
            stmt = self.declaration()
            if stmt is not None:
                statements.append(stmt)

        self.consume(TokenType.RIGHT_BRACE, "Expect '}' after block.")
        return statements

    def assignment(self) -> Optional[Expr]:
        expr = self.pipe()

        if self.match(TokenType.EQUAL):
            equals = self.previous()
            value = self.assignment()

            if isinstance(expr, Variable):
                name = expr.name
                return Assign(name, value)

            raise self.error(equals, "Invalid assignment target.")

        return expr

    def pipe(self) -> Optional[Expr]:
        # `a |> f` desugars directly to the call `f(a)` at parse time --
        # there's no separate Pipe AST node or interpreter support needed.
        # Chains left-associatively: `a |> f |> g` is `g(f(a))`.
        expr = self.comma()

        while self.match(TokenType.PIPE):
            operator = self.previous()
            right = self.comma()
            if not isinstance(right, Variable):
                raise self.error(operator, "Expect a function reference after '|>'.")
            expr = Call(right, [expr])

        return expr

    def comma(self) -> Optional[Expr]:
        # While disabled (inside a `[...]` vector literal), a single element
        # is just a ternary -- the comma between elements is the vector
        # loop's own separator, not this operator, and is left untouched
        # for that loop to consume.
        if not self.comma_as_operator:
            return self.ternary()

        expr = self.ternary()

        while self.match(TokenType.COMMA):
            operator = self.previous()
            right = self.ternary()
            expr = Binary(expr, operator, right)

        return expr

    def ternary(self) -> Optional[Expr]:
        expr = self.null_coalescing()

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

    def null_coalescing(self) -> Optional[Expr]:
        expr = self.logic_or()

        while self.match(TokenType.DOUBLE_QUESTION_MARK):
            operator = self.previous()
            right = self.logic_or()
            expr = Binary(expr, operator, right)

        return expr

    def logic_or(self) -> Optional[Expr]:
        expr = self.logic_and()

        while self.match(TokenType.OR):
            operator = self.previous()
            right = self.logic_and()
            expr = Logical(expr, operator, right)

        return expr

    def logic_and(self) -> Optional[Expr]:
        expr = self.equality()

        while self.match(TokenType.AND):
            operator = self.previous()
            right = self.equality()
            expr = Logical(expr, operator, right)

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
        ):
            operator = self.previous()
            right = self.increment()
            expr = Binary(expr, operator, right)

        return expr

    def increment(self) -> Optional[Expr]:
        if self.match(TokenType.PLUS_PLUS, TokenType.MINUS_MINUS):
            operator = self.previous()
            right = self.unary()
            if not isinstance(right, Variable):
                raise self.error(operator, "Invalid increment/decrement target.")
            return Unary(operator, right)

        return self.unary()

    def unary(self) -> Optional[Expr]:
        if self.match(TokenType.BANG, TokenType.MINUS):
            operator = self.previous()
            right = self.unary()
            return Unary(operator, right)

        return self.call()

    def call(self) -> Optional[Expr]:
        # No call takes parentheses. `f()` is the explicit zero-arg marker;
        # `f a`, `f a, b` (fixed-arity, comma-separated, no wrapping parens)
        # are calls with arguments; a bare `f` with nothing recognizable as
        # an argument following it is just a value reference, not a call.
        if self.check(TokenType.IDENTIFIER):
            if self.check_at(1, TokenType.LEFT_PAREN) and self.check_at(2, TokenType.RIGHT_PAREN):
                name = self.advance()
                self.advance()
                self.advance()
                return Call(Variable(name), [])

            if self.starts_argument(self.peek_at(1)):
                name = self.advance()
                arguments = [self.argument()]
                while self.match(TokenType.COMMA):
                    arguments.append(self.argument())
                return Call(Variable(name), arguments)

        return self.primary()

    def argument(self) -> Optional[Expr]:
        # A grouped expression, a nested call, or a bare primary -- `call()`
        # already covers all three, falling through to `primary()`, which
        # handles `"(" expression ")"` as a `Grouping`.
        return self.call()

    @staticmethod
    def starts_argument(token: Token) -> bool:
        return token.type in ARGUMENT_START_TOKENS

    def primary(self) -> Optional[Expr]:
        if self.match(TokenType.FALSE):
            return Literal(False)
        if self.match(TokenType.TRUE):
            return Literal(True)
        if self.match(TokenType.NIL):
            return Literal(None)
        if self.match(TokenType.BREAK):
            return Break()
        if self.match(TokenType.CONTINUE):
            return Continue()
        if self.match(TokenType.UNDERSCORE):
            return Ignore()

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

            if not self.check(TokenType.RIGHT_BRACKET):
                exprs.append(self.expression())
                while self.match(TokenType.COMMA):
                    exprs.append(self.expression())

            self.comma_as_operator = True
            self.consume(TokenType.RIGHT_BRACKET, "Expect ']' after vector elements.")
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

    def check_at(self, offset: int, _type: TokenType) -> bool:
        return self.peek_at(offset).type == _type

    def advance(self) -> Token:
        if not self.is_at_end():
            self.current += 1
        return self.previous()

    def is_at_end(self) -> bool:
        return self.peek().type == TokenType.EOF

    def peek(self) -> Token:
        return self.tokens[self.current]

    def peek_at(self, offset: int) -> Token:
        index = min(self.current + offset, len(self.tokens) - 1)
        return self.tokens[index]

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
                TokenType.RETURN
            ]:
                return

            self.advance()

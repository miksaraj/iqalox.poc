from typing import List, Any

from token import (
    Token,
    TokenType,
    SINGLE_CHARACTER_TOKENS,
    ONE_OR_MORE_CHARACTER_TOKENS,
    WHITESPACE,
    STRING_STARTERS,
    KEYWORDS,
    COMMENT_TOKENS
)


class Scanner:
    def __init__(self, source: str) -> None:
        self.source = source
        self.tokens: List[Token] = []
        self.start = 0
        self.current = 0
        self.line = 1

    @property
    def current_token(self) -> str:
        return self.source[self.start:self.current]

    def is_at_end(self) -> bool:
        return self.current >= len(self.source)

    def advance(self) -> str:
        self.current += 1
        return self.current_token

    def add_token(self, token_type: TokenType, literal: Any = None) -> None:
        self.tokens.append(Token(token_type, self.current_token, literal, self.line))

    def match(self, expected: str) -> bool:
        if self.is_at_end() or self.source[self.current] != expected:
            return False
        self.current += 1
        return True

    def peek(self) -> str:
        if self.is_at_end():
            return '\0'
        return self.source[self.current]

    def peek_next(self) -> str:
        if self.current + 1 >= len(self.source):
            return '\0'
        return self.source[self.current + 1]

    def is_digit(self) -> bool:
        return '0' <= self.peek() <= '9'

    def is_alpha(self) -> bool:
        c = self.peek()
        return ('a' <= c <= 'z') or ('A' <= c <= 'Z') or c == '_'

    def is_alpha_numeric(self) -> bool:
        return self.is_alpha() or self.is_digit()

    def string(self, char: str) -> None:
        while self.peek() != char and not self.is_at_end():
            if self.peek() == '\n':
                self.line += 1
            self.advance()
        if self.is_at_end():
            import iqalox
            iqalox.Iqalox.error(Token(TokenType.EOF, '', None, self.line), "Unterminated string.")
            return
        self.advance()
        value = self.source[self.start + 1:self.current - 1]
        self.add_token(TokenType.STRING, value)

    def number(self) -> None:
        while self.is_digit():
            self.advance()
        if self.peek() == '.' and self.is_digit():
            self.advance()
            while self.is_digit():
                self.advance()
        self.add_token(TokenType.NUMBER, float(self.source[self.start:self.current]))

    def identifier(self) -> None:
        while self.is_alpha_numeric():
            self.advance()
        text = self.current_token
        token_type = KEYWORDS.get(text, TokenType.IDENTIFIER)
        self.add_token(token_type)

    def scan_token(self) -> None:
        c = self.advance()
        if c in SINGLE_CHARACTER_TOKENS:
            self.add_token(TokenType(c))
        elif c in ONE_OR_MORE_CHARACTER_TOKENS:
            compounds = [i for i in ONE_OR_MORE_CHARACTER_TOKENS if i.startswith(c) and len(i) > 1]
            token = c
            for compound in compounds:
                if self.match(compound[1]):
                    token = compound
                    break
            if token in COMMENT_TOKENS:
                if token == TokenType.COMMENT:
                    while self.peek() != '\n' and not self.is_at_end():
                        self.advance()
                elif token == TokenType.BLOCK_COMMENT_START:
                    while self.peek() != '#' and self.peek_next() != '>' and not self.is_at_end():
                        self.advance()
                    self.advance()
                    self.advance()
            else:
                self.add_token(TokenType(token))
        elif c in WHITESPACE:
            return
        elif c == TokenType.NEWLINE:
            self.add_token(TokenType.SEMICOLON)
            self.line += 1
        elif c in STRING_STARTERS:
            self.string(c)
        elif self.is_digit():
            self.number()
        elif self.is_alpha():
            self.identifier()
        else:
            # TODO [#2]: handle a run of one or more invalid tokens as a single error.
            import iqalox
            iqalox.Iqalox.error(Token(TokenType.NULL_CHAR, c, None, self.line), f"Unexpected character: {c}")

    def scan_tokens(self) -> List[Token]:
        while not self.is_at_end():
            self.start = self.current
            self.scan_token()
        self.tokens.append(Token(TokenType.EOF, "", None, self.line))
        return self.tokens

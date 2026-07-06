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
        # Source offset where the current line began -- lets column_at() turn
        # an absolute offset into a 1-indexed column within that line.
        self.line_start = 0
        # Line/column of the token currently being scanned, captured once at
        # the top of scan_tokens()'s loop -- used (rather than self.line/
        # column_at(self.start) at add_token() time) so a token that itself
        # spans multiple lines (e.g. a multi-line string) is still reported
        # at the position it *started*, not wherever scanning ended up.
        self.start_line = 1
        self.start_column = 1

    @property
    def current_token(self) -> str:
        return self.source[self.start:self.current]

    def column_at(self, offset: int) -> int:
        return offset - self.line_start + 1

    def advance_newline(self) -> None:
        # Call right after advance() has consumed a '\n', so self.current
        # already points just past it -- keeps self.line_start/self.line in
        # sync wherever a newline is consumed outside the main scan_token()
        # dispatch (inside strings and block comments).
        self.line += 1
        self.line_start = self.current

    def is_at_end(self) -> bool:
        return self.current >= len(self.source)

    def advance(self) -> str:
        self.current += 1
        return self.current_token

    def add_token(self, token_type: TokenType, literal: Any = None) -> None:
        self.tokens.append(Token(token_type, self.current_token, literal, self.start_line, self.start_column))

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

    def is_digit(self, c: str = None) -> bool:
        if c is None:
            c = self.peek()
        return '0' <= c <= '9'

    def is_alpha(self, c: str = None) -> bool:
        if c is None:
            c = self.peek()
        return ('a' <= c <= 'z') or ('A' <= c <= 'Z') or c == '_'

    def is_alpha_numeric(self, c: str = None) -> bool:
        return self.is_alpha(c) or self.is_digit(c)

    def is_recognized(self, c: str) -> bool:
        # Anything scan_token()'s dispatch would actually know what to do
        # with -- used to find where a run of otherwise-unrecognized
        # characters ends (see the fallback branch in scan_token()).
        return (
            c in SINGLE_CHARACTER_TOKENS
            or c in ONE_OR_MORE_CHARACTER_TOKENS
            or c in WHITESPACE
            or c in STRING_STARTERS
            or c == TokenType.NEWLINE
            or self.is_digit(c)
            or self.is_alpha(c)
        )

    def string(self, char: str) -> None:
        while self.peek() != char and not self.is_at_end():
            if self.peek() == '\n':
                self.advance()
                self.advance_newline()
            else:
                self.advance()
        if self.is_at_end():
            import iqalox
            # Reports at the opening quote's own position (not wherever
            # end-of-file was actually reached), since that's where the user
            # needs to look to fix it.
            iqalox.Iqalox.error(
                Token(TokenType.STRING, char, None, self.start_line, self.start_column), "Unterminated string."
            )
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
                    while not (self.peek() == '#' and self.peek_next() == '>') and not self.is_at_end():
                        if self.peek() == '\n':
                            self.advance()
                            self.advance_newline()
                        else:
                            self.advance()
                    self.advance()
                    self.advance()
            else:
                try:
                    self.add_token(TokenType(token))
                except ValueError:
                    # `token` didn't extend into a longer valid token (e.g. a
                    # bare '|' not followed by '>') and isn't valid on its own.
                    import iqalox
                    iqalox.Iqalox.error(
                        Token(TokenType.NULL_CHAR, c, None, self.start_line, self.start_column),
                        f"Unexpected character: {c}",
                    )
        elif c in WHITESPACE:
            return
        elif c == TokenType.NEWLINE:
            self.add_token(TokenType.SEMICOLON)
            self.advance_newline()
        elif c in STRING_STARTERS:
            self.string(c)
        elif self.is_digit(c):
            self.number()
        elif self.is_alpha(c):
            self.identifier()
        else:
            # A character with no recognized meaning on its own. Keep
            # consuming as long as what follows is *also* unrecognized, so a
            # whole run of garbage characters (e.g. `@@@`) is reported as one
            # error instead of one per character.
            while not self.is_at_end() and not self.is_recognized(self.peek()):
                self.advance()
            import iqalox
            iqalox.Iqalox.error(
                Token(TokenType.NULL_CHAR, self.current_token, None, self.start_line, self.start_column),
                f"Unexpected character{'s' if len(self.current_token) > 1 else ''}: {self.current_token}",
            )

    def scan_tokens(self) -> List[Token]:
        while not self.is_at_end():
            self.start = self.current
            self.start_line = self.line
            self.start_column = self.column_at(self.start)
            self.scan_token()
        self.tokens.append(Token(TokenType.EOF, "", None, self.line, self.column_at(self.current)))
        return self.tokens

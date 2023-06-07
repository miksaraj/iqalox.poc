from enum import Enum
from typing import Dict, Any, Tuple


class TokenType(Enum):
    # Single-character tokens.
    LEFT_PAREN = '('
    RIGHT_PAREN = ')'
    LEFT_BRACE = '{'
    RIGHT_BRACE = '}'
    COMMA = ','
    SEMICOLON = ';'
    SLASH = '/'
    BACKSLASH = '\\'
    STAR = '*'
    UNDERSCORE = '_'
    PERCENT = '%'
    POWER = '^'

    # One or two+ character tokens.
    BANG = '!'
    BANG_EQUAL = '!='
    EQUAL = '='
    EQUAL_EQUAL = '=='
    GREATER = '>'
    GREATER_EQUAL = '>='
    LESS = '<'
    LESS_EQUAL = '<='
    MINUS = '-'
    MINUS_MINUS = '--'
    PLUS = '+'
    PLUS_PLUS = '++'
    DOT = '.'
    ELLIPSIS = '...'
    QUESTION_MARK = '?'
    COLON = ':'
    QUESTION_MARK_COLON = '?:'
    DOUBLE_QUESTION_MARK = '??'
    PIPE = '|>'

    # Keywords.
    AND = 'and'
    CLASS = 'class'
    FALSE = 'false'
    FUN = 'fun'
    FOR = 'for'
    NIL = 'nil'
    OR = 'or'
    PRINT = 'print'
    RETURN = 'return'
    SUPER = 'super'
    SELF = 'self'
    TRUE = 'true'
    VAR = 'var'
    WITH = 'with'
    CONCAT = 'concat'
    MODULE = 'module'
    TRAIT = 'trait'
    EXTENDS = 'extends'
    BREAK = 'break'
    CONTINUE = 'continue'
    USE = 'use'

    # String starters.
    SINGLE_QUOTE = "'"
    DOUBLE_QUOTE = '"'

    # New line.
    NEWLINE = '\n'

    # Space.
    SPACE = ' '
    TAB = '\t'

    # String terminator
    NULL_CHAR = '\0'

    # EOF
    EOF = ''

    # Comment
    COMMENT = '#'
    BLOCK_COMMENT_START = '<#'
    BLOCK_COMMENT_END = '#>'

    # Literals
    IDENTIFIER = 'IDENTIFIER'
    STRING = 'STRING'
    NUMBER = 'NUMBER'


_keywords: Tuple = (
    'and', 'class', 'false', 'fun', 'for', 'nil', 'or', 'print', 'return', 'super', 'self', 'true', 'var', 'with',
    'concat', 'module', 'trait', 'extends', 'break', 'continue', 'use'
)

KEYWORDS: Dict[str, TokenType] = {key: TokenType(key) for key in _keywords}

SINGLE_CHARACTER_TOKENS: Tuple = ('(', ')', '{', '}', ',', ';', '/', '\\', '*', '_')

ONE_OR_MORE_CHARACTER_TOKENS: Tuple = (
    '!', '!=', '=', '==', '>', '>=', '<', '<=', '-', '--', '+',
    '++', '.', '...', '?', ':', '?:', '??', '|>', '#', '<#', '#>'
)

COMMENT_TOKENS: Tuple = ('#', '<#', '#>')

WHITESPACE: Tuple = (' ', '\t', '\r')

STRING_STARTERS: Tuple = ("'", '"')


class Token:
    def __init__(self, token_type: TokenType, lexeme: str, literal: Any, line: int):
        self.type = token_type
        self.lexeme = lexeme
        self.literal = literal
        self.line = line

    def __str__(self) -> str:
        return f"{self.type}: {self.lexeme}, {self.literal}, {self.line}"

    def __repr__(self) -> str:
        properties = f"{self.type}, {self.lexeme}, {self.literal}, {self.line}"
        return f'{self.__class__.__name__}({properties})'

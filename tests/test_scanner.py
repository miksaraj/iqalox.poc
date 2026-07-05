from scanner import Scanner
from token import TokenType


def token_types(source: str):
    return [t.type for t in Scanner(source).scan_tokens()]


def test_newline_becomes_semicolon():
    assert token_types("var x = 1\nprint x") == [
        TokenType.VAR, TokenType.IDENTIFIER, TokenType.EQUAL, TokenType.NUMBER, TokenType.SEMICOLON,
        TokenType.IDENTIFIER, TokenType.IDENTIFIER, TokenType.EOF,
    ]


def test_blank_lines_do_not_duplicate_semicolons_meaninglessly():
    # A blank line still yields a semicolon token (an empty statement), but
    # it must not poison the scan of what follows.
    tokens = token_types("var x = 1\n\nprint x")
    assert tokens.count(TokenType.SEMICOLON) == 2
    assert TokenType.IDENTIFIER in tokens


def test_single_character_identifiers_and_numbers():
    # Regression test: the scanner used to test the *next* character (via
    # peek()) instead of the one just consumed when deciding whether to
    # start scanning a number/identifier, so any single-char token adjacent
    # to a delimiter (`i`, `1`, `5`, ...) was misreported as "unexpected".
    tokens = Scanner("i").scan_tokens()
    assert tokens[0].type == TokenType.IDENTIFIER
    assert tokens[0].lexeme == "i"

    tokens = Scanner("5").scan_tokens()
    assert tokens[0].type == TokenType.NUMBER
    assert tokens[0].literal == 5.0


def test_line_comment_is_skipped():
    # The comment text itself contributes no tokens; the newline that ends
    # the comment line still yields its own (empty-statement) semicolon,
    # same as it would for a blank line.
    tokens = token_types("# a comment\nprint 1")
    assert tokens == [TokenType.SEMICOLON, TokenType.IDENTIFIER, TokenType.NUMBER, TokenType.EOF]


def test_block_comment_is_skipped():
    tokens = token_types("<# a\nmultiline\ncomment #>\nprint 1")
    assert tokens == [TokenType.SEMICOLON, TokenType.IDENTIFIER, TokenType.NUMBER, TokenType.EOF]


def test_block_comment_terminator_requires_adjacent_hash_and_angle():
    # Regression test: the terminator loop used to stop on a lone '#' or on
    # any character followed by '>', not just on the literal "#>" sequence.
    tokens = token_types("<# 100% not > done # yet #>\nprint 1")
    assert tokens == [TokenType.SEMICOLON, TokenType.IDENTIFIER, TokenType.NUMBER, TokenType.EOF]


def test_block_comment_tracks_line_numbers():
    tokens = Scanner("<# line one\nline two #>\nprint 1").scan_tokens()
    print_token = next(t for t in tokens if t.type == TokenType.IDENTIFIER)
    assert print_token.line == 3

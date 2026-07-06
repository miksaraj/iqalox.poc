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


def test_pipe_operator_scans_as_one_token():
    # Regression test: the scanner's dispatch requires the single character
    # just consumed to itself be a listed token before it looks for a
    # longer match -- '|' alone wasn't listed (only the full '|>' was), so
    # this always fell through to "Unexpected character" instead.
    tokens = token_types("a |> b")
    assert tokens[1] == TokenType.PIPE


def test_bare_pipe_character_is_a_clean_scan_error_not_a_crash():
    # '|' not followed by '>' doesn't extend into any valid token; this
    # must be reported like any other bad character, not raise a raw
    # ValueError from an unguarded TokenType(token) construction.
    tokens = token_types("a | b")
    assert TokenType.PIPE not in tokens


def test_token_tracks_column_within_its_line():
    tokens = Scanner("var x = 1").scan_tokens()
    ident = next(t for t in tokens if t.type == TokenType.IDENTIFIER)
    assert ident.lexeme == "x"
    assert ident.column == 5  # 1-indexed: 'v','a','r',' ','x'


def test_token_column_resets_on_each_new_line():
    tokens = Scanner("var x = 1\n  y = 2").scan_tokens()
    y_token = next(t for t in tokens if t.type == TokenType.IDENTIFIER and t.lexeme == "y")
    assert y_token.line == 2
    assert y_token.column == 3  # two leading spaces on the second line


def test_multiline_string_is_reported_at_its_opening_quote(capsys):
    Scanner('var s = "unterminated\nstill going').scan_tokens()
    out = capsys.readouterr().out
    # The opening quote is on line 1, even though the scanner only notices
    # the string is unterminated after running off the end of the source.
    assert "[line 1]" in out
    assert "Unterminated string" in out


def test_run_of_invalid_characters_is_a_single_error(capsys):
    tokens = token_types("var x = @@@ 1")
    out = capsys.readouterr().out
    # One error, not three -- and it names the whole run, not just one '@'.
    assert out.count("Unexpected character") == 1
    assert "@@@" in out
    assert TokenType.NUMBER in tokens  # scanning recovers and continues past the run


def test_single_invalid_character_is_still_reported_singular(capsys):
    token_types("var x = @ 1")
    out = capsys.readouterr().out
    assert "Unexpected character:" in out
    assert "Unexpected characters:" not in out


def test_decimal_number_literals_scan_as_a_single_token():
    # Regression test: number()'s fractional-part check used to compare
    # self.peek() against a digit test that *also* defaulted to
    # self.peek() -- the same '.' character checked against itself, never
    # the one after it -- so the fractional branch could never fire.
    # "3.14" scanned as NUMBER("3"), DOT, NUMBER("14").
    tokens = Scanner("3.14").scan_tokens()
    assert [t.type for t in tokens] == [TokenType.NUMBER, TokenType.EOF]
    assert tokens[0].lexeme == "3.14"
    assert tokens[0].literal == 3.14


def test_a_trailing_dot_with_no_following_digit_is_not_consumed_as_a_fraction():
    tokens = token_types("5.")
    assert tokens == [TokenType.NUMBER, TokenType.DOT, TokenType.EOF]


def test_leading_underscore_identifier_scans_as_one_identifier():
    # Regression test: '_' is checked as its own token before the
    # identifier dispatch runs, so "_foo" used to misscan as a bare '_'
    # (the ignore operator) followed by a separate "foo" identifier.
    tokens = Scanner("_foo").scan_tokens()
    assert [t.type for t in tokens] == [TokenType.IDENTIFIER, TokenType.EOF]
    assert tokens[0].lexeme == "_foo"


def test_bare_underscore_is_still_the_ignore_operator():
    assert token_types("_") == [TokenType.UNDERSCORE, TokenType.EOF]
    assert token_types("_?") == [TokenType.UNDERSCORE, TokenType.QUESTION_MARK, TokenType.EOF]


def test_ellipsis_consumes_exactly_three_characters():
    # Regression test: the compound-matching dispatch used to only ever
    # advance one extra character regardless of a compound's actual
    # length, so "..." under-consumed to "..". Moot today (no grammar uses
    # "...' yet) but worth not leaving broken.
    tokens = Scanner("...").scan_tokens()
    assert [t.type for t in tokens] == [TokenType.ELLIPSIS, TokenType.EOF]
    assert tokens[0].lexeme == "..."


def test_bare_hash_angle_outside_a_block_comment_starts_a_line_comment():
    # Regression test: a bare '#' immediately followed by '>' used to be
    # mismatched as the BLOCK_COMMENT_END compound at the top level (which
    # only has meaning as a terminator searched for *inside* a block
    # comment), silently swallowing both characters with no token or error
    # at all. '#' must always start a line comment on its own.
    tokens = token_types("5 #> comment\nprint 1")
    assert tokens == [
        TokenType.NUMBER, TokenType.SEMICOLON, TokenType.IDENTIFIER, TokenType.NUMBER, TokenType.EOF,
    ]


def test_unterminated_block_comment_is_a_scan_error(capsys):
    # Regression test: an unterminated block comment used to produce no
    # error at all -- the loop just exited silently at EOF, unlike an
    # unterminated string, which already errored correctly.
    tokens = Scanner("<# never closed").scan_tokens()
    out = capsys.readouterr().out
    assert "Unterminated block comment" in out
    assert [t.type for t in tokens] == [TokenType.EOF]

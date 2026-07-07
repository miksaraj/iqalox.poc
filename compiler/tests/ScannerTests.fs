module ScannerTests

open Xunit
open Iqalox.Token
open Iqalox.Scanner

let private tokenTypes (source: string) =
    let tokens, _ = scanTokens source
    tokens |> List.map (fun t -> t.Type)

[<Fact>]
let ``newline becomes a semicolon`` () =
    Assert.Equal<TokenType list>(
        [ Var; Identifier; Equal; Number; Semicolon; Identifier; Identifier; Eof ],
        tokenTypes "var x = 1\nprint x"
    )

[<Fact>]
let ``blank lines still yield their own semicolon without poisoning what follows`` () =
    let types = tokenTypes "var x = 1\n\nprint x"
    Assert.Equal(2, types |> List.filter (fun t -> t = Semicolon) |> List.length)
    Assert.Contains(Identifier, types)

[<Fact>]
let ``single-character identifiers and numbers scan correctly`` () =
    let tokens, errors = scanTokens "i"
    Assert.Empty(errors)
    Assert.Equal(Identifier, tokens.[0].Type)
    Assert.Equal("i", tokens.[0].Lexeme)

    let tokens, errors = scanTokens "5"
    Assert.Empty(errors)
    Assert.Equal(Number, tokens.[0].Type)
    Assert.Equal(NumberLiteral 5.0, tokens.[0].Literal)

[<Fact>]
let ``decimal number literals scan as a single token`` () =
    // Regression test for a real poc bug: poc/src/scanner.py's number()
    // compares self.peek() to a digit check that also defaults to
    // self.peek(), so it never actually looks at the character after the
    // dot -- "3.14" scans as NUMBER("3"), DOT, NUMBER("14") in poc.
    let tokens, errors = scanTokens "3.14"
    Assert.Empty(errors)
    Assert.Equal<TokenType list>([ Number; Eof ], tokens |> List.map (fun t -> t.Type))
    Assert.Equal(NumberLiteral 3.14, tokens.[0].Literal)
    Assert.Equal("3.14", tokens.[0].Lexeme)

[<Fact>]
let ``a trailing dot with no following digit is not consumed as a fraction`` () =
    let tokens, _ = scanTokens "5."
    Assert.Equal<TokenType list>([ Number; Dot; Eof ], tokens |> List.map (fun t -> t.Type))

[<Fact>]
let ``line comment is skipped`` () =
    Assert.Equal<TokenType list>([ Semicolon; Identifier; Number; Eof ], tokenTypes "# a comment\nprint 1")

[<Fact>]
let ``block comment is skipped`` () =
    Assert.Equal<TokenType list>(
        [ Semicolon; Identifier; Number; Eof ],
        tokenTypes "<# a\nmultiline\ncomment #>\nprint 1"
    )

[<Fact>]
let ``block comment terminator requires the adjacent hash and angle bracket`` () =
    // Regression test mirroring poc's own: the terminator search must not
    // stop on a lone '#' or on any character followed by '>'.
    Assert.Equal<TokenType list>(
        [ Semicolon; Identifier; Number; Eof ],
        tokenTypes "<# 100% not > done # yet #>\nprint 1"
    )

[<Fact>]
let ``block comment tracks line numbers`` () =
    let tokens, _ = scanTokens "<# line one\nline two #>\nprint 1"
    let printToken = tokens |> List.find (fun t -> t.Type = Identifier)
    Assert.Equal(3, printToken.Line)

[<Fact>]
let ``an unterminated block comment is a scan error`` () =
    // poc doesn't report this at all; the corresponding position is
    // silently dropped. Fixed here.
    let _, errors = scanTokens "<# never closed"
    Assert.Single(errors) |> ignore
    Assert.Contains("Unterminated block comment", errors.[0].Message)

[<Fact>]
let ``pipe operator scans as one token`` () =
    Assert.Equal(Pipe, (tokenTypes "a |> b").[1])

[<Fact>]
let ``a bare pipe character is a clean scan error, not a crash`` () =
    let types = tokenTypes "a | b"
    Assert.DoesNotContain(Pipe, types)

[<Fact>]
let ``token tracks its column within the line`` () =
    let tokens, _ = scanTokens "var x = 1"
    let ident = tokens |> List.find (fun t -> t.Type = Identifier)
    Assert.Equal("x", ident.Lexeme)
    Assert.Equal(5, ident.Column) // 1-indexed: 'v','a','r',' ','x'

[<Fact>]
let ``token column resets on each new line`` () =
    let tokens, _ = scanTokens "var x = 1\n  y = 2"
    let yToken = tokens |> List.find (fun t -> t.Type = Identifier && t.Lexeme = "y")
    Assert.Equal(2, yToken.Line)
    Assert.Equal(3, yToken.Column)

[<Fact>]
let ``a run of invalid characters is a single error naming the whole run`` () =
    let tokens, errors = scanTokens "var x = @@@ 1"
    Assert.Single(errors) |> ignore
    Assert.Contains("@@@", errors.[0].Message)
    Assert.Contains("Unexpected characters", errors.[0].Message)
    Assert.Contains(Number, tokens |> List.map (fun t -> t.Type)) // scanning recovers afterward

[<Fact>]
let ``a single invalid character is reported in the singular`` () =
    let _, errors = scanTokens "var x = @ 1"
    Assert.Single(errors) |> ignore
    Assert.Contains("Unexpected character:", errors.[0].Message)
    Assert.DoesNotContain("Unexpected characters:", errors.[0].Message)

[<Fact>]
let ``a leading underscore identifier scans as a single identifier`` () =
    // Regression test for a real poc bug: '_' is checked as its own token
    // before the identifier dispatch runs, so "_foo" misscans as a bare
    // '_' (the ignore operator) followed by a separate "foo" identifier.
    let tokens, errors = scanTokens "_foo"
    Assert.Empty(errors)
    Assert.Equal<TokenType list>([ Identifier; Eof ], tokens |> List.map (fun t -> t.Type))
    Assert.Equal("_foo", tokens.[0].Lexeme)

[<Fact>]
let ``a bare underscore is still the ignore operator`` () =
    Assert.Equal<TokenType list>([ Underscore; Eof ], tokenTypes "_")
    Assert.Equal<TokenType list>([ Underscore; QuestionMark; Eof ], tokenTypes "_?")

[<Fact>]
let ``ellipsis consumes exactly three characters`` () =
    // Regression test for a real poc bug: its generic compound-matching
    // only ever advances one extra character regardless of the matched
    // compound's length, so "..." under-consumes to ".." in poc.
    let tokens, errors = scanTokens "..."
    Assert.Empty(errors)
    Assert.Equal<TokenType list>([ Ellipsis; Eof ], tokens |> List.map (fun t -> t.Type))
    Assert.Equal("...", tokens.[0].Lexeme)

[<Fact>]
let ``elvis and full ternary operators scan distinctly`` () =
    Assert.Equal<TokenType list>(
        [ Identifier; QuestionMarkColon; Identifier; Eof ],
        tokenTypes "a ?: b"
    )
    Assert.Equal<TokenType list>(
        [ Identifier; QuestionMark; Identifier; Colon; Identifier; Eof ],
        tokenTypes "a ? b : c"
    )

[<Fact>]
let ``arrow scans distinctly from minus and minus-minus`` () =
    // docs/PLAN-0.2.md decision 1's lambda syntax (`(a, b) -> expr`) --
    // longest-match already picks the 2-char "->" over the 1-char "-"
    // via Scanner.fs's generic operatorCandidates table, no special
    // lookahead logic needed.
    Assert.Equal<TokenType list>([ Identifier; Arrow; Identifier; Eof ], tokenTypes "x -> y")
    Assert.Equal<TokenType list>([ Identifier; Minus; Identifier; Eof ], tokenTypes "x - y")
    Assert.Equal<TokenType list>([ Identifier; MinusMinus; Eof ], tokenTypes "x--")

[<Fact>]
let ``keywords are recognized and near-misses are not`` () =
    Assert.Equal<TokenType list>([ Self; Eof ], tokenTypes "self")
    Assert.Equal<TokenType list>([ Identifier; Eof ], tokenTypes "selfish")

[<Theory>]
[<InlineData(@"\n", "\n")>]
[<InlineData(@"\t", "\t")>]
[<InlineData(@"\r", "\r")>]
[<InlineData(@"\\", "\\")>]
[<InlineData(@"\'", "'")>]
[<InlineData(@"\""", "\"")>]
[<InlineData(@"\0", "\000")>]
let ``recognized escape sequences resolve to the expected character`` (escape: string, expected: string) =
    let tokens, errors = scanTokens ("\"" + escape + "\"")
    Assert.Empty(errors)
    Assert.Equal(StringLiteral expected, tokens.[0].Literal)

[<Fact>]
let ``an unrecognized escape sequence is a hard error`` () =
    let _, errors = scanTokens "\"\\q\""
    Assert.Single(errors) |> ignore
    Assert.Contains("Unrecognized escape sequence", errors.[0].Message)

[<Fact>]
let ``an unterminated string is a scan error`` () =
    let _, errors = scanTokens "\"never closed"
    Assert.Single(errors) |> ignore
    Assert.Contains("Unterminated string", errors.[0].Message)

[<Fact>]
let ``a string can span multiple raw lines`` () =
    let tokens, errors = scanTokens "\"line one\nline two\""
    Assert.Empty(errors)
    Assert.Equal(StringLiteral "line one\nline two", tokens.[0].Literal)

[<Fact>]
let ``single and double quoted strings are both accepted`` () =
    Assert.Equal<TokenType list>([ String; Eof ], tokenTypes "'hi'")
    Assert.Equal<TokenType list>([ String; Eof ], tokenTypes "\"hi\"")

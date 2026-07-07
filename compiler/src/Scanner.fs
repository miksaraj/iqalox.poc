/// Scanner: source text -> `Token` list.
///
/// Ports `poc/src/scanner.py`'s design, including its two proven-correct
/// post-release fixes (accurate line/column tracking via a `lineStart`
/// offset; coalescing a run of unrecognized characters into a single
/// error instead of one per character), plus escape sequences in string
/// literals (new for `0.1`, see `docs/PLAN-0.1.md` decision 5).
///
/// This is a fresh implementation, not a line-for-line port, because
/// writing it carefully surfaced several `poc` bugs that were never
/// "proven correct" and so aren't carried forward:
///   - Decimal number literals never worked in `poc` at all (`number()`
///     compares `self.peek()` against a digit check that also defaults to
///     `self.peek()`, i.e. it always compares the same '.' character to
///     itself and never looks at what follows it) -- `3.14` scans as
///     `NUMBER("3")`, `DOT`, `NUMBER("14")`. Fixed here by checking the
///     character *after* the dot.
///   - A leading-underscore identifier (`_foo`) misscans as a bare `_`
///     (the ignore operator) followed by a separate `foo` identifier,
///     since `_` is checked as its own token before the identifier
///     dispatch runs. Fixed here by checking whether an alphanumeric
///     character follows before deciding `_` is standalone.
///   - The `...` (ellipsis) token under-consumes by one character (`poc`'s
///     generic compound-matching only ever advances one extra character
///     regardless of the compound's actual length) -- moot in `poc` since
///     `...` has no grammar yet, but worth not carrying forward. Fixed
///     here by a longest-match search that consumes exactly the matched
///     lexeme's length.
///   - A bare `#>` outside a block comment is silently swallowed with no
///     token or error (an accident of treating comment markers as just
///     another entry in the general operator-compound table). Fixed here
///     by giving comments their own explicit dispatch instead.
///   - An unterminated block comment (`<#` with no matching `#>` before
///     EOF) produces no error at all in `poc`. Fixed here to report one,
///     matching how an unterminated string is already handled.
module Iqalox.Scanner

open System.Text
open Iqalox.Token

type ScanError = { Message: string; Line: int; Column: int }

/// Multi- and single-character punctuation, longest lexeme first so a
/// longest-match search finds e.g. `...` before `.`, or `??` before `?`.
/// Comment markers (`#`, `<#`, `#>`) and string quotes are deliberately
/// not here -- `Scanner`'s main dispatch handles those explicitly instead
/// of through this generic table (see the module doc comment).
let private operatorCandidates: (string * TokenType) list =
    [ "...", Ellipsis
      "!=", BangEqual
      "==", EqualEqual
      ">=", GreaterEqual
      "<=", LessEqual
      "--", MinusMinus
      "->", Arrow
      "<-", LeftArrow
      "++", PlusPlus
      "?:", QuestionMarkColon
      "??", DoubleQuestionMark
      "|>", Pipe
      "(", LeftParen
      ")", RightParen
      "{", LeftBrace
      "}", RightBrace
      "[", LeftBracket
      "]", RightBracket
      ",", Comma
      ";", Semicolon
      "/", Slash
      "\\", Backslash
      "*", Star
      "%", Percent
      "^", Power
      "!", Bang
      "=", Equal
      ">", Greater
      "<", Less
      "-", Minus
      "+", Plus
      ".", Dot
      "?", QuestionMark
      ":", Colon
      "|", VerticalBar ]
    |> List.sortByDescending (fun (lexeme, _) -> lexeme.Length)

let private operatorStartChars: Set<char> =
    operatorCandidates |> List.map (fun (lexeme, _) -> lexeme.[0]) |> Set.ofList

let private isDigit (c: char) = c >= '0' && c <= '9'
let private isAlpha (c: char) = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c = '_'
let private isAlphaNumeric (c: char) = isAlpha c || isDigit c

/// Recognized escape sequences (decision 5, docs/PLAN-0.1.md): the common
/// C-family set. Anything else after a backslash is a hard error -- no
/// silent fallback to a literal backslash-plus-character.
let private escapeSequences: Map<char, char> =
    Map.ofList [ 'n', '\n'; 't', '\t'; 'r', '\r'; '\\', '\\'; '\'', '\''; '"', '"'; '0', '\000' ]

type private ScannerState(source: string) =
    let tokens = ResizeArray<Token>()
    let errors = ResizeArray<ScanError>()
    let mutable start = 0
    let mutable current = 0
    let mutable line = 1
    let mutable lineStart = 0
    // Line/column of the token currently being scanned, captured once per
    // token (mirrors `poc`'s `start_line`/`start_column`) so a token that
    // itself spans multiple lines (a multi-line string, a block comment)
    // is still reported at the position it *started*, not wherever
    // scanning ended up.
    let mutable startLine = 1
    let mutable startColumn = 1

    let isAtEnd () = current >= source.Length

    let peek () = if isAtEnd () then '\000' else source.[current]

    let peekNext () =
        if current + 1 >= source.Length then '\000' else source.[current + 1]

    let advance () =
        let c = source.[current]
        current <- current + 1
        c

    let columnAt (offset: int) = offset - lineStart + 1

    /// Call right after `advance()` has consumed a '\n', so `current`
    /// already points just past it.
    let advanceNewline () =
        line <- line + 1
        lineStart <- current

    let currentLexeme () = source.Substring(start, current - start)

    let addToken (tokenType: TokenType) (literal: Literal) =
        tokens.Add(
            { Type = tokenType
              Lexeme = currentLexeme ()
              Literal = literal
              Line = startLine
              Column = startColumn }
        )

    let addError (message: string) (errLine: int) (errColumn: int) =
        errors.Add { Message = message; Line = errLine; Column = errColumn }

    let isRecognized (c: char) =
        c = '\n'
        || c = ' '
        || c = '\t'
        || c = '\r'
        || c = '#'
        || c = '\''
        || c = '"'
        || isDigit c
        || isAlpha c
        || Set.contains c operatorStartChars

    let scanUnrecognizedRun () =
        while not (isAtEnd ()) && not (isRecognized (peek ())) do
            advance () |> ignore
        let lexeme = currentLexeme ()
        let suffix = if lexeme.Length > 1 then "s" else ""
        addError $"Unexpected character{suffix}: {lexeme}" startLine startColumn

    let tryMatchOperator () : TokenType option =
        let remaining = source.Length - start
        operatorCandidates
        |> List.tryFind (fun (lexeme, _) -> lexeme.Length <= remaining && source.Substring(start, lexeme.Length) = lexeme)
        |> Option.map (fun (lexeme, tokenType) ->
            current <- start + lexeme.Length
            tokenType)

    let scanLineComment () =
        while peek () <> '\n' && not (isAtEnd ()) do
            advance () |> ignore

    let scanBlockComment () =
        // `current` is already past "<#" by the time this is called.
        let mutable closed = false
        while not closed && not (isAtEnd ()) do
            if peek () = '#' && peekNext () = '>' then
                advance () |> ignore
                advance () |> ignore
                closed <- true
            elif peek () = '\n' then
                advance () |> ignore
                advanceNewline ()
            else
                advance () |> ignore
        if not closed then
            addError "Unterminated block comment." startLine startColumn

    let scanString (quote: char) =
        let value = StringBuilder()
        let mutable finished = false
        // Distinct from `finished`: on a bad escape we keep scanning to
        // find the real closing quote (so scanning resumes at the right
        // place afterward) but stop producing a String token, and only
        // report the first bad escape in a given string rather than one
        // per occurrence -- same "don't spam" philosophy as coalescing a
        // run of unrecognized characters elsewhere in this scanner.
        let mutable hadError = false
        while not finished do
            if isAtEnd () then
                addError "Unterminated string." startLine startColumn
                finished <- true
            elif peek () = quote then
                advance () |> ignore
                finished <- true
            elif peek () = '\\' then
                let backslashColumn = columnAt (current)
                advance () |> ignore // consume the backslash
                if isAtEnd () then
                    addError "Unterminated string." startLine startColumn
                    finished <- true
                else
                    let escapeChar = advance ()
                    match Map.tryFind escapeChar escapeSequences with
                    | Some resolved -> value.Append(resolved) |> ignore
                    | None ->
                        if not hadError then
                            addError $"Unrecognized escape sequence: \\{escapeChar}" line backslashColumn
                        hadError <- true
            elif peek () = '\n' then
                value.Append('\n') |> ignore
                advance () |> ignore
                advanceNewline ()
            else
                value.Append(advance ()) |> ignore
        if not hadError then
            addToken String (StringLiteral(value.ToString()))

    let scanNumber () =
        while isDigit (peek ()) do
            advance () |> ignore
        if peek () = '.' && isDigit (peekNext ()) then
            advance () |> ignore // consume the '.'
            while isDigit (peek ()) do
                advance () |> ignore
        addToken Number (NumberLiteral(float (currentLexeme ())))

    let scanIdentifierOrKeyword () =
        while isAlphaNumeric (peek ()) do
            advance () |> ignore
        let text = currentLexeme ()
        match Map.tryFind text keywords with
        | Some tokenType -> addToken tokenType NoLiteral
        | None -> addToken Identifier NoLiteral

    let scanToken () =
        let c = advance ()
        if c = '\n' then
            addToken Semicolon NoLiteral
            advanceNewline ()
        elif c = ' ' || c = '\t' || c = '\r' then
            ()
        elif c = '#' then
            scanLineComment ()
        elif c = '<' && peek () = '#' then
            advance () |> ignore
            scanBlockComment ()
        elif c = '\'' || c = '"' then
            scanString c
        elif isDigit c then
            scanNumber ()
        elif c = '_' then
            if isAlphaNumeric (peek ()) then
                scanIdentifierOrKeyword ()
            else
                addToken Underscore NoLiteral
        elif isAlpha c then
            scanIdentifierOrKeyword ()
        else
            match tryMatchOperator () with
            | Some tokenType -> addToken tokenType NoLiteral
            | None -> scanUnrecognizedRun ()

    member _.ScanTokens() : Token list * ScanError list =
        while not (isAtEnd ()) do
            start <- current
            startLine <- line
            startColumn <- columnAt start
            scanToken ()

        tokens.Add(
            { Type = Eof
              Lexeme = ""
              Literal = NoLiteral
              Line = line
              Column = columnAt current }
        )

        List.ofSeq tokens, List.ofSeq errors

/// Scans `source` into a list of tokens plus any scan errors. Scanning
/// never throws -- a bad character or an unterminated string/comment is
/// recorded as a `ScanError` and scanning continues, matching `poc`'s
/// "report and keep going" approach (mirroring parse-error recovery, not
/// an exception-per-error design).
let scanTokens (source: string) : Token list * ScanError list =
    ScannerState(source).ScanTokens()

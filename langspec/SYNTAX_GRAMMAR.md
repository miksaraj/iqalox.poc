# Syntax Grammar for Iqalox (0.2)
*WIP — target spec, phased into `compiler/`+`vm/` incrementally
(`docs/PLAN-0.2.md` §5); see that document's §4 feature checklist for
what has actually landed so far.*

This is `0.1`'s grammar (`langspec/versions/0.1/SYNTAX_GRAMMAR.md`) plus
everything `docs/PLAN-0.2.md` §1/§3 resolved for `0.2`: postfix indexing,
lambdas, cons/list-comprehension vector forms, vector-literal spread,
`pub`/`mut` property declarations, method visibility, and mixin (`with`)/
trait (`trait`/`use`) composition. Notes carried over from `0.1` (no
`if`/`while`, no parenthesized calls, etc.) are repeated below rather than
requiring readers to cross-reference the old file for baseline rules that
are still exactly true.

## Notes: inherited from `0.1`, unchanged

* There is no `if`/`while` statement: the chainable ternary operator
  (see `expression`/`ternary` below) replaces `if`/`else` entirely,
  including for statement-like branches such as `break`/`continue`
  (implemented as expressions, not statements — see
  `docs/PLAN-0.1-POC.md` decision 1); `while` was removed from the
  language outright (`for` is the only loop construct).
* No function call (builtin or user-defined) takes parentheses — see the
  `call`/`arguments`/`argument` rules below. Parens still appear for:
  grouping a compound argument (`fact (n - 1)`), the explicit zero-arg
  call marker (`count()`), function *declarations'* parameter lists
  (`funDecl`/`function`/`method`, unchanged), and now also lambda
  parameter lists (`lambda`, new this version — see below).
* `call` chains `.` member access/method calls and (new this version)
  `[...]` indexing off of a `call_head`, in any combination/order —
  `v[0][1]`, `obj.getVec()[0]`, `matrix[i][j]` are all just repeated
  postfix steps on the same production.
* Chaining `.method()` (or, new this version, `[expr]`) straight onto a
  call that itself takes arguments is still ambiguous in this paren-free
  grammar and binds to the argument, not the outer call — unchanged from
  `0.1` (`docs/PLAN-0.1-POC.md` §1/§2); bind to a variable first, or wrap
  the call in its own parens, to chain immediately.
* `%` (modulo) and `^` (power) sit at the same precedence level as `/`/`*`
  (both left-associative), inside `multiplication` below.
* No `undef` literal appears in `primary` — it's an implicit runtime
  value (`docs/LANGUAGE.md` §3-4), not something user code writes.

## Notes: new for `0.2`

* **Indexing as an assignment target** (`v[i] = x`) follows the same
  parsing pattern `0.1`'s `self.x = value` already uses: parse a `call`
  (which may itself already end in `.identifier` or `[expr]` postfix
  steps as an ordinary *read*), then peel the trailing `[expr]` off
  specifically when it's immediately followed by `=`, and treat *that*
  step as the mutable target — not a general "any call is assignable"
  rule. See the `assignment` production below.
* **`v[0]` (indexing) and `v [0]` (a call with a vector-literal argument)
  are told apart by whitespace, resolved during Phase 1 implementation
  after this document's first draft missed the collision entirely.**
  `0.1`'s call syntax already lets a bare identifier/property be "called"
  with a vector literal as its sole argument (`concat [1, 2]`, used
  throughout every example) — that parses to the exact same token stream
  as postfix indexing on the same identifier. A `[` with no space before
  it is always indexing; a `[` with a space is always the pre-existing
  call form, completely unchanged. This is the only place whitespace is
  significant anywhere in Iqalox's grammar — see `docs/PLAN-0.2.md`
  decision 6's addendum for the full reasoning.
* **Lambda vs. a grouped comma expression share the same opening
  `(`.** `0.1`'s comma operator already makes `(a, b)` a valid grouped
  expression (evaluate `a`, discard it, yield `b`) — `docs/PLAN-0.2.md`
  decision 1's `(a, b) -> expr` lambda syntax reuses the identical
  `(IDENTIFIER, IDENTIFIER, ...)` shape for its parameter list. These are
  told apart by lookahead **past** the closing `)`: only if the very next
  token is `->` is the parenthesized list re-read as a lambda's
  parameters; otherwise it's an ordinary grouped/comma expression.
* **Cons (`[item | list]`) and list comprehensions (`[expr | x <- xs]`)
  share the same `[expr |` prefix**, per decision 2 — disambiguated by
  lookahead on the generator marker `<-` immediately after `|`. Decision
  3 restricts `0.2`'s comprehensions to a single generator with no
  guards; `docs/ROADMAP.md`'s `0.3` entry has the eventual
  multi-generator/guarded shape.
* **The comprehension's bound name is grammared here as a single
  `IDENTIFIER`**, not a general destructuring pattern. `docs/PLAN-0.2.md`
  decision 3 borrows the word "pattern" from the Haskell-style sketch this
  feature is modeled on, but Iqalox has no destructuring/pattern-matching
  syntax anywhere else in the language, and nothing in the plan document
  actually specifies one for this feature either — this is the grammar's
  working interpretation of that wording, not a separately confirmed
  decision. Flag before Phase 3 (`docs/PLAN-0.2.md` §5) if a real pattern
  syntax was intended instead.
* **Vector-literal spread (`[...a, ...b]`) only applies inside the
  plain, comma-separated item-list form of a vector literal** — it does
  not combine with cons or comprehension syntax (`[...a | b]` is not
  meaningful under decision 7's scope, which is vector-literal spread
  specifically).
* **Property declarations take no initializer expression at all.**
  Every example under decision 8 (`var name`, `var name mut`, `var name
  pub`, `var name pub mut`) omits one, and what a `var x pub = value`
  initializer would even mean per-instance (a shared default vs.
  something evaluated fresh per construction) is never addressed in
  `docs/PLAN-0.2.md` — so the grammar below has no production for one at
  all, rather than guessing at a shape. A property's actual value comes
  from whatever assignment (typically inside `init`) happens to run
  first, per decision 9.
* **Trait bodies (`trait T { ... }`) are grammared identically to class
  bodies** — properties, nested `use`, and methods — since PHP's own
  `trait`s support both fields and methods and `docs/PLAN-0.2.md`
  decision 12 doesn't restrict `0.2`'s version to methods only. This is
  an inference from the PHP-parity framing the decision itself uses, not
  a separately spelled-out choice.
* **`init`'s always-externally-callable rule (decision 11) is semantic,
  not grammatical.** `init` is written exactly like any other `method` —
  with or without `pub` — and there is no dedicated grammar rule for it;
  the "external calls to it always succeed regardless of the annotation"
  behavior is a resolver/VM rule, not a parse-time distinction.
* **Trait conflict resolution and mixin linearization order are not
  grammar concerns.** `docs/PLAN-0.2.md` §2.1 (static `use` conflicts)
  and §2.2 (dynamic `with` linearization algorithm) are open questions
  about what a program *means*, not what parses — both `use A, B` and
  `with M1, M2` parse unambiguously today regardless of how those
  questions resolve.
##
    program         → declaration* EOF ;

    declaration     → classDecl
                    | traitDecl
                    | funDecl
                    | varDecl
                    | statement ;

    classDecl       → "class" IDENTIFIER ( "extends" IDENTIFIER )?
                       ( "with" IDENTIFIER ( "," IDENTIFIER )* )?
                       "{" classMember* "}" ;
    traitDecl       → "trait" IDENTIFIER "{" classMember* "}" ;
    classMember     → propertyDecl
                    | traitUse
                    | method ;
    propertyDecl    → "var" IDENTIFIER "pub"? "mut"? ";?" ;
    traitUse        → "use" IDENTIFIER ( "," IDENTIFIER )* ";?" ;
    method          → "pub"? IDENTIFIER "(" parameters? ")" block ;

    funDecl         → "fun" function ;
    function        → IDENTIFIER "(" parameters? ")" block ;
    parameters      → IDENTIFIER ( "," IDENTIFIER )* ;
    varDecl         → "var" IDENTIFIER "mut"? ( "=" expression )? ";?" ;

    statement       → exprStmt
                    | forStmt
                    | returnStmt
                    | block ;

    exprStmt        → expression ";?" ;
    forStmt         → "for" "(" ( varDecl | exprStmt | ";" )
                                expression? ";?"
                                expression? ")" statement ;
    returnStmt      → "return" expression? ";?" ;
    block           → "{" declaration* "}" ;

    expression      → assignment ;

    assignment      → ( call "." )? IDENTIFIER "=" assignment
                    | call "[" expression "]" "=" assignment
                    | pipe ;
    pipe            → comma ( "|>" IDENTIFIER )* ;
    comma           → ternary ( "," ternary )* ;

    ternary         → null_coalescing ( "?" expression ":" expression
                                       | "?:" expression )? ;
    null_coalescing → logic_or ( "??" logic_or )* ;
    logic_or        → logic_and ( "or" logic_and )* ;
    logic_and       → equality ( "and" equality )* ;
    equality        → comparison ( ( "!=" | "==" ) comparison )* ;
    comparison      → addition ( ( ">" | ">=" | "<" | "<=" ) addition )* ;
    addition        → multiplication ( ( "-" | "+" ) multiplication )* ;
    multiplication  → increment ( ( "/" | "*" | "%" | "^" ) increment )* ;
    increment       → ( "++" | "--" ) unary | unary ;

    unary           → ( "!" | "-" ) unary | call ;
    call            → call_head ( "." IDENTIFIER arguments?
                                 | "[" expression "]" )* ;
    call_head       → IDENTIFIER arguments?
                    | primary ;
    arguments       → "(" ")"
                    | argument ( "," argument )* ;
    argument        → "(" expression ")"
                    | primary
                    | call ;
    primary         → "true" | "false" | "nil" | "self"
                    | "break" | "continue" | "_"
                    | NUMBER | STRING | IDENTIFIER
                    | lambda
                    | "(" expression ")"
                    | vector
                    | "super" "." IDENTIFIER ;

    lambda          → "(" parameters? ")" "->" expression ;

    vector          → "[" vectorBody? "]" ;
    vectorBody      → vectorItem ( "," vectorItem )*
                    | expression "|" expression
                    | expression "|" IDENTIFIER "<-" expression ;
    vectorItem      → "..." expression
                    | expression ;

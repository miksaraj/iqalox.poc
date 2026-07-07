/// The AST `Parser` produces. Mirrors the node shapes
/// `poc/src/expression.py`/`poc/src/statement.py` generate (see
/// `poc/tools/generate_ast.py`), but as plain F# discriminated unions
/// instead of a generated visitor-pattern class hierarchy -- pattern
/// matching replaces the visitor dispatch, with no code-generation step
/// needed.
module Iqalox.Ast

open Iqalox.Token

/// A literal expression's resolved value. Distinct from `Token.Literal`
/// (which only ever holds what the *scanner* attaches to a single NUMBER/
/// STRING token) -- this covers every literal `primary` can produce,
/// including `true`/`false`/`nil`, which the scanner hands back as plain
/// keyword tokens with no literal payload of their own.
type LiteralValue =
    | NilValue
    | BoolValue of bool
    | NumberValue of float
    | StringValue of string

/// Case names ending in `Expr` (`BreakExpr`, `ContinueExpr`, `SelfExpr`,
/// `SuperExpr`) would otherwise collide with `Token.TokenType`'s own
/// `Break`/`Continue`/`Self`/`Super` cases -- both modules are typically
/// opened together, and F# resolves a bare, ambiguous case name to
/// whichever `open` came last, silently breaking the other. Same story
/// for `Stmt` below (`Class`/`Var`/`For`/`Return`, suffixed `Stmt`).
/// Every other case name here is unambiguous and left bare.
type Expr =
    | Assign of name: Token * value: Expr
    | Binary of left: Expr * operator: Token * right: Expr
    | Logical of left: Expr * operator: Token * right: Expr
    | Grouping of expression: Expr
    | Literal of value: LiteralValue
    | Unary of operator: Token * right: Expr
    | Ternary of left: Expr * leftOperator: Token * middle: Expr * rightOperator: Token * right: Expr
    | Vector of values: Expr list
    | Variable of name: Token
    | BreakExpr of keyword: Token
    | ContinueExpr of keyword: Token
    | Ignore
    | Call of callee: Expr * arguments: Expr list
    | Get of obj: Expr * name: Token
    | Set of obj: Expr * name: Token * value: Expr
    | Index of obj: Expr * index: Expr * bracket: Token
    | IndexSet of obj: Expr * index: Expr * value: Expr * bracket: Token
    | Lambda of parameters: Token list * arrow: Token * body: Expr
    | SelfExpr of keyword: Token
    | SuperExpr of keyword: Token * method: Token

/// Shared between `Stmt.FunctionStmt` (a top-level `fun` declaration) and
/// `Stmt.ClassStmt`'s `methods` field -- `poc`'s `Class.methods` is typed
/// as `List[Function]`, a Python type hint with no runtime enforcement;
/// here it's a real, checked type, since a class's methods are never
/// anything but function declarations.
type FunctionDecl = { Name: Token; Parameters: Token list; Body: Stmt list }

and Stmt =
    | Block of statements: Stmt list
    | ExpressionStmt of expression: Expr
    | VarStmt of name: Token * initializer: Expr option * isMutable: bool
    | ForStmt of initializer: Stmt option * condition: Expr option * increment: Expr option * body: Stmt
    | FunctionStmt of FunctionDecl
    | ReturnStmt of keyword: Token * value: Expr option
    | ClassStmt of name: Token * superclass: Expr option * methods: FunctionDecl list

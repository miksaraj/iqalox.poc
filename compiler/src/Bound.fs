/// The output of `Resolver`: a tree shaped exactly like `Ast.Expr`/
/// `Ast.Stmt`, except every variable reference/assignment/`self`/`super`
/// now carries a `VariableBinding` saying exactly where it lives (a
/// compile-time-known stack slot, a closed-over upvalue, or a
/// name-addressed global), and every declaration carries a
/// `DeclaredBinding` saying where *it* creates a slot. `Codegen` (Phase 5)
/// consumes this directly instead of re-deriving scope information from
/// the raw parsed AST.
module Iqalox.Bound

open Iqalox.Token
open Iqalox.Ast

/// Where a variable *reference* (a read, or the target of an assignment)
/// resolves to.
type VariableBinding =
    /// A stack slot in the current function's own frame.
    | LocalBinding of slot: int
    /// A variable closed over from an enclosing function -- `index` is
    /// this function's own upvalue list index, not the enclosing
    /// function's local slot (see `UpvalueDescriptor`).
    | UpvalueBinding of index: int
    /// Not found as a local or upvalue anywhere up the enclosing-function
    /// chain -- resolved dynamically by name at runtime, exactly like
    /// every variable in `poc`.
    | GlobalBinding of name: string

/// Where a variable *declaration* (`var`, a top-level `fun`/`class` name)
/// creates its binding. Never `UpvalueBinding` -- a declaration always
/// creates something new in the current function's locals or in the
/// global table, never closes over an existing one.
type DeclaredBinding =
    | DeclaredLocal of slot: int
    | DeclaredGlobal of name: string

/// How a closure captures one upvalue when it's created at runtime:
/// either directly from a slot in the *immediately* enclosing function's
/// frame (`FromEnclosingLocal = true`), or from an upvalue the enclosing
/// function itself already captured (`FromEnclosingLocal = false`) -- the
/// same two-case design `clox` uses (Crafting Interpreters ch. 25) to let
/// a doubly (or more) nested closure capture a variable from further up
/// the chain than its immediate parent.
type UpvalueDescriptor = { FromEnclosingLocal: bool; Index: int }

type BoundExpr =
    | BAssign of binding: VariableBinding * name: Token * value: BoundExpr
    | BBinary of left: BoundExpr * operator: Token * right: BoundExpr
    | BLogical of left: BoundExpr * operator: Token * right: BoundExpr
    | BGrouping of expression: BoundExpr
    | BLiteral of value: LiteralValue
    | BUnary of operator: Token * right: BoundExpr
    | BTernary of left: BoundExpr * leftOperator: Token * middle: BoundExpr * rightOperator: Token * right: BoundExpr
    | BVector of values: BoundExpr list
    | BVariable of binding: VariableBinding * name: Token
    | BBreak of keyword: Token
    | BContinue of keyword: Token
    | BIgnore
    | BCall of callee: BoundExpr * arguments: BoundExpr list
    | BGet of obj: BoundExpr * name: Token
    | BSet of obj: BoundExpr * name: Token * value: BoundExpr
    | BSelf of binding: VariableBinding * keyword: Token
    | BSuper of selfBinding: VariableBinding * binding: VariableBinding * keyword: Token * method: Token

/// `LocalCount` is how many stack slots the VM must reserve for this
/// function's frame (including its own implicit `self` slot for methods);
/// `Upvalues` tells the VM how to populate each captured variable when a
/// closure over this function is created.
type BoundFunctionDecl =
    { Name: Token
      Parameters: Token list
      Body: BoundStmt list
      LocalCount: int
      Upvalues: UpvalueDescriptor list }

and BoundStmt =
    | BBlock of statements: BoundStmt list
    | BExpressionStmt of expression: BoundExpr
    | BVarStmt of binding: DeclaredBinding * name: Token * initializer: BoundExpr option
    | BForStmt of initializer: BoundStmt option * condition: BoundExpr option * increment: BoundExpr option * body: BoundStmt
    | BFunctionStmt of binding: DeclaredBinding * decl: BoundFunctionDecl
    | BReturnStmt of keyword: Token * value: BoundExpr option
    | BClassStmt of
        binding: DeclaredBinding *
        name: Token *
        superclass: (VariableBinding * Token) option *
        methods: BoundFunctionDecl list

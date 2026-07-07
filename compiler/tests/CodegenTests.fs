module CodegenTests

open Xunit
open Iqalox.Scanner
open Iqalox.Parser
open Iqalox.Resolver
open Iqalox.Bound
open Iqalox.Bytecode
open Iqalox.Codegen
open Iqalox.Disassembler

let private compileSource (source: string) : Chunk =
    let source = if source.EndsWith "\n" then source else source + "\n"
    let tokens, _ = scanTokens source
    let stmts, _ = parse tokens
    let bound, resolveErrors = resolve stmts
    Assert.Empty resolveErrors
    let chunk, codegenErrors = compile bound
    Assert.Empty codegenErrors
    chunk

[<Fact>]
let ``a bare literal expression statement pushes, pops, then implicitly returns nil`` () =
    let chunk = compileSource "1"
    Assert.Equal<Instruction[]>([| Constant 0; Pop; Nil; Return |], chunk.Code)
    Assert.Equal<Constant[]>([| NumberConstant 1.0 |], chunk.Constants)

[<Fact>]
let ``each instruction's Lines entry matches the source line it came from`` () =
    // Regression test for the diagnostics gap found while writing
    // docs/LANGUAGE.md's Phase 10 entry: bytecode used to carry no line
    // info at all, so a runtime fault couldn't be blamed on any `[line
    // N]`. `var x = 1` is line 1, `x` (the bare reference) is line 2; the
    // implicit trailing `Nil; Return` inherits line 2 too, since nothing
    // after `x` ever updates CurrentLine again.
    let chunk = compileSource "var x = 1\nx"
    Assert.Equal<Instruction[]>(
        [| Constant 0; DefineGlobal 1; GetGlobal 1; Pop; Nil; Return |],
        chunk.Code
    )
    Assert.Equal<int[]>([| 1; 1; 2; 2; 2; 2 |], chunk.Lines)

[<Fact>]
let ``a global var declares via DefineGlobal and reads back via GetGlobal`` () =
    let chunk = compileSource "var x = 1\nx"
    Assert.Equal<Instruction[]>([| Constant 0; DefineGlobal 1; GetGlobal 1; Pop; Nil; Return |], chunk.Code)
    Assert.Equal<Constant[]>([| NumberConstant 1.0; StringConstant "x" |], chunk.Constants)

[<Fact>]
let ``a local var needs no store instruction, and a block cleans its own slots up with PopN`` () =
    let chunk = compileSource "{ var x = 1\nx; }"
    Assert.Equal<Instruction[]>([| Constant 0; GetLocal 0; Pop; PopN 1; Nil; Return |], chunk.Code)

[<Fact>]
let ``the comma operator evaluates and discards the left side, fixing poc's always-nil bug`` () =
    let chunk = compileSource "(1, 2)"
    Assert.Equal<Instruction[]>([| Constant 0; Pop; Constant 1; Pop; Nil; Return |], chunk.Code)
    Assert.Equal<Constant[]>([| NumberConstant 1.0; NumberConstant 2.0 |], chunk.Constants)

[<Fact>]
let ``?? short-circuits via JumpIfNotNil instead of always evaluating both sides`` () =
    let chunk = compileSource "nil ?? 5"
    Assert.Equal<Instruction[]>([| Nil; JumpIfNotNil 4; Pop; Constant 0; Pop; Nil; Return |], chunk.Code)

[<Fact>]
let ``elvis evaluates its condition exactly once, fixing poc's double-evaluation bug`` () =
    let chunk = compileSource "1 ?: 2"
    // Only one `Constant 0` for the condition -- not two -- is the bug fix
    // itself: poc's `visit_ternary_expr` evaluates `left` a second time to
    // produce `middle` even though the parser gave them the same node.
    Assert.Equal<Instruction[]>(
        [| Constant 0; JumpIfFalse 3; Jump 5; Pop; Constant 1; Pop; Nil; Return |],
        chunk.Code
    )
    Assert.Equal<Constant[]>([| NumberConstant 1.0; NumberConstant 2.0 |], chunk.Constants)

[<Fact>]
let ``a full ternary evaluates its condition once and branches to exactly one arm`` () =
    let chunk = compileSource "1 ? 2 : 3"
    Assert.Equal<Instruction[]>(
        [| Constant 0; JumpIfFalse 5; Pop; Constant 1; Jump 7; Pop; Constant 2; Pop; Nil; Return |],
        chunk.Code
    )

[<Fact>]
let ``and short-circuits on a falsy left operand, keeping it instead of evaluating right`` () =
    let chunk = compileSource "1 and 2"
    Assert.Equal<Instruction[]>([| Constant 0; JumpIfFalse 4; Pop; Constant 1; Pop; Nil; Return |], chunk.Code)

[<Fact>]
let ``or short-circuits on a truthy left operand, keeping it instead of evaluating right`` () =
    let chunk = compileSource "1 or 2"
    Assert.Equal<Instruction[]>(
        [| Constant 0; JumpIfFalse 3; Jump 5; Pop; Constant 1; Pop; Nil; Return |],
        chunk.Code
    )

[<Fact>]
let ``prefix increment reads, adds one, and stores back, leaving the new value on the stack`` () =
    let chunk = compileSource "{ var x mut = 1\n++x; }"
    Assert.Equal<Instruction[]>(
        [| Constant 0 // x = 1
           GetLocal 0
           Constant 0 // literal 1 -- dedups against x's own initializer constant
           Add
           SetLocal 0
           Pop // expression-statement discard
           PopN 1 // block cleanup
           Nil
           Return |],
        chunk.Code
    )

[<Fact>]
let ``a for loop's condition-false exit and a break both unwind to the same depth without double-popping`` () =
    let chunk = compileSource "for (var i = 0; i < 3; ++i) i;"
    Assert.Equal<Instruction[]>(
        [| Constant 0 // i = 0
           GetLocal 0 // <- loopStart: condition
           Constant 1 // literal 3
           Less
           JumpIfFalse 14
           Pop // discard truthy condition
           GetLocal 0 // body: `i;`
           Pop
           GetLocal 0 // increment: ++i
           Constant 2 // literal 1
           Add
           SetLocal 0
           Pop
           Jump 1 // back to loopStart
           Pop // discard falsy condition
           PopN 1 // pop the loop-scoped `i`
           Nil
           Return |],
        chunk.Code
    )
    Assert.Equal<Constant[]>([| NumberConstant 0.0; NumberConstant 3.0; NumberConstant 1.0 |], chunk.Constants)

[<Fact>]
let ``break jumps past the loop's own condition-false cleanup, popping its own way out inline instead`` () =
    let chunk = compileSource "for (var i = 0;;) { break; }"
    Assert.Equal<Instruction[]>(
        [| Constant 0 // i = 0
           PopN 1 // break: pop `i` before jumping out
           Jump 6
           Nil // break's own (dead) expression-value placeholder
           Pop // expression-statement discard (dead)
           Jump 1 // loop back (dead, unreachable -- break always fires first)
           Nil
           Return |],
        chunk.Code
    )

[<Fact>]
let ``a function's own frame starts at parameter count, needing no explicit parameter stores`` () =
    let chunk = compileSource "fun f(a) { return a; }"
    Assert.Equal<Instruction[]>([| Closure(0, []); DefineGlobal 1; Nil; Return |], chunk.Code)

    match chunk.Constants with
    | [| FunctionConstant proto; StringConstant "f" |] ->
        Assert.Equal("f", proto.Name)
        Assert.Equal(1, proto.Arity)
        Assert.Equal<Instruction[]>([| GetLocal 0; Return; Nil; Return |], proto.Chunk.Code)
    | constants -> failwith $"unexpected constants: %A{constants}"

[<Fact>]
let ``super.method() pushes self and the superclass, resolved independently, then emits GetSuper`` () =
    let source = "class A { greet() { return 1; } }\nclass B extends A {\n    greet() { return super.greet(); }\n}"
    let chunk = compileSource source

    Assert.Equal<Instruction[]>(
        [| Class 0 // "A"
           DefineGlobal 0
           GetGlobal 0 // temp re-fetch of A
           Closure(1, []) // A.greet
           Method 2 // "greet"
           Pop
           Class 3 // "B"
           DefineGlobal 3
           GetGlobal 0 // superclass A
           GetGlobal 3 // temp re-fetch of B
           Inherit
           Closure(4, [ { FromEnclosingLocal = true; Index = 0 } ]) // B.greet, capturing the synthetic `super` local
           Method 2 // "greet"
           Pop
           Pop // discards the synthetic `super` local -- its scope (Resolver.fs's beginScope/endScope) ends here
           Nil
           Return |],
        chunk.Code
    )

    match chunk.Constants with
    | [| StringConstant "A"; FunctionConstant greetA; StringConstant "greet"; StringConstant "B"; FunctionConstant greetB |] ->
        Assert.Equal<Instruction[]>([| Constant 0; Return; Nil; Return |], greetA.Chunk.Code)
        // self (LocalBinding 0) then super (UpvalueBinding 0, captured from
        // the enclosing scope's own synthetic `super` local) -- resolved
        // independently of one another, per Resolver.fs's SuperExpr case.
        Assert.Equal<Instruction[]>(
            [| GetLocal 0; GetUpvalue 0; GetSuper 0; Call 0; Return; Nil; Return |],
            greetB.Chunk.Code
        )
    | constants -> failwith $"unexpected constants: %A{constants}"

[<Fact>]
let ``two independent superclass declarations in the same script both capture slot 0`` () =
    // Regression test for a real bug: Resolver.fs's synthetic `super`
    // local is scoped to just its own class declaration (added, then
    // removed via beginScope/endScope wrapping the superclass expression
    // and that class's own methods) -- so a *second*, unrelated `extends`
    // later in the same script reuses the exact same slot 0, not slot 1.
    // Codegen must pop the first class's superclass value once its own
    // methods are compiled, or the runtime stack would still have it
    // sitting in slot 0 when the second class's superclass is pushed
    // there too, corrupting that second class's captured `super` upvalue.
    let source =
        "class Base1 { value() { return 1; } }\n"
        + "class A extends Base1 { greet() { return super.value(); } }\n"
        + "class Base2 { value() { return 2; } }\n"
        + "class Sub extends Base2 { greet() { return super.value(); } }"

    let chunk = compileSource source

    let greetProtos =
        chunk.Constants
        |> Array.choose (function
            | FunctionConstant proto when proto.Name = "greet" -> Some proto
            | _ -> None)

    Assert.Equal(2, greetProtos.Length)
    for proto in greetProtos do
        Assert.Equal<UpvalueDescriptor list>([ { FromEnclosingLocal = true; Index = 0 } ], proto.Upvalues)

[<Fact>]
let ``the disassembler prints every top-level instruction and recurses into nested function chunks`` () =
    let chunk = compileSource "fun f() { return 1; }"
    let output = disassembleChunk "script" chunk

    Assert.Contains("== script ==", output)
    Assert.Contains("CLOSURE", output)
    Assert.Contains("== f ==", output)
    Assert.Contains("CONSTANT 0 (1)", output)

[<Fact>]
let ``break outside any loop is a codegen error, not a crash`` () =
    let tokens, _ = scanTokens "break\n"
    let stmts, _ = parse tokens
    let bound, resolveErrors = resolve stmts
    Assert.Empty resolveErrors
    let _, codegenErrors = compile bound
    Assert.Single codegenErrors |> ignore
    Assert.Contains("outside of a loop", codegenErrors.[0].Message)
    // Regression test for the diagnostics gap found while writing
    // docs/LANGUAGE.md's Phase 10 entry: codegen errors used to carry no
    // line number at all. `break` itself is a token-less BBreak case, so
    // this also exercises that its keyword's line makes it through.
    Assert.Equal(1, codegenErrors.[0].Line)

[<Fact>]
let ``a codegen error reports the line of the statement it occurred on, not line 1`` () =
    let tokens, _ = scanTokens "var x = 1\nvar y = 2\nbreak\n"
    let stmts, _ = parse tokens
    let bound, resolveErrors = resolve stmts
    Assert.Empty resolveErrors
    let _, codegenErrors = compile bound
    Assert.Single codegenErrors |> ignore
    Assert.Equal(3, codegenErrors.[0].Line)

[<Fact>]
let ``indexing pushes obj and index, then emits GetIndex with no operand`` () =
    let chunk = compileSource "var v = [1, 2]\nv[0]"
    Assert.Equal<Instruction[]>(
        [| Constant 0; Constant 1; BuildVector 2; DefineGlobal 2; GetGlobal 2; Constant 3; GetIndex; Pop; Nil; Return |],
        chunk.Code
    )
    Assert.Equal<Constant[]>(
        [| NumberConstant 1.0; NumberConstant 2.0; StringConstant "v"; NumberConstant 0.0 |],
        chunk.Constants
    )

[<Fact>]
let ``indexed assignment pushes obj, index, and value, then emits SetIndex with no operand`` () =
    let chunk = compileSource "var v = [1, 2]\nv[0] = 9"
    Assert.Equal<Instruction[]>(
        [| Constant 0
           Constant 1
           BuildVector 2
           DefineGlobal 2
           GetGlobal 2
           Constant 3
           Constant 4
           SetIndex
           Pop
           Nil
           Return |],
        chunk.Code
    )
    Assert.Equal<Constant[]>(
        [| NumberConstant 1.0; NumberConstant 2.0; StringConstant "v"; NumberConstant 0.0; NumberConstant 9.0 |],
        chunk.Constants
    )

[<Fact>]
let ``a lambda compiles to a Closure over an implicit-return function, no new opcode`` () =
    let chunk = compileSource "var square = (n) -> n * n"
    Assert.Equal<Instruction[]>([| Closure(0, []); DefineGlobal 1; Nil; Return |], chunk.Code)
    match chunk.Constants.[0] with
    | FunctionConstant proto ->
        Assert.Equal("lambda", proto.Name)
        Assert.Equal(1, proto.Arity)
        Assert.Equal<Instruction[]>([| GetLocal 0; GetLocal 0; Multiply; Return; Nil; Return |], proto.Chunk.Code)
    | other -> failwith $"expected a FunctionConstant, got %A{other}"
    Assert.Equal<Constant>(StringConstant "square", chunk.Constants.[1])

/// `docs/PLAN-0.2.md` Phase 5's array-manipulation standard library.
/// `push`/`pop`/`length`/`reverse` need direct access to an `ObjVector`'s
/// own element storage, so they're true natives (`vm/src/natives.cpp`,
/// registered in `Resolver.fs`'s `nativeGlobals`). `map`/`filter`/
/// `reduce`/`sort` are different: each needs to call back into a
/// user-supplied lambda and get its result, and a native function's
/// current calling convention has no way to do that -- `Vm::callNative`
/// invokes `native->function` synchronously and expects a `Value` back
/// immediately, while calling a closure (`Vm::call`) only ever queues a
/// new `CallFrame` for the *existing* bytecode dispatch loop to pick up
/// later; there's no "call this closure and synchronously get its result"
/// primitive for native C++ code to use.
///
/// Rather than build that (a real, untested new VM capability), these
/// four are just ordinary Iqalox source, written using the same surface
/// syntax any user program has access to -- a loop that calls the lambda
/// via perfectly normal `Call` bytecode, exactly like Phase 3's `Cons`/
/// `ListComprehension` already prove out for a different case (a
/// synthetic closure looping over a runtime-unknown-length vector). The
/// difference here is these don't need Resolver.fs-level desugaring at
/// all: they're literal `fun` declarations, textually prepended to every
/// program's own source (`Program.fs`) and resolved/compiled as part of
/// the very same program -- no special-casing anywhere in Resolver.fs or
/// Codegen.fs, and (unlike `print`/`concat`/`push`/`pop`/`length`/
/// `reverse`) not listed in `Resolver.fs`'s `nativeGlobals` either, since
/// they don't need pre-registration -- an ordinary top-level `fun`
/// already resolves to a global on its own.
///
/// Two real consequences of this approach, both accepted for this phase
/// rather than solved: (1) a runtime error raised from *inside* one of
/// these four's own body reports a `[line N]` number relative to this
/// module's own source text below, not anything in the user's actual
/// file -- logged as a known limitation (`docs/LANGUAGE.md` §13) rather
/// than solved with real multi-file source-position tracking, which
/// nothing in this pipeline has ever needed before now. (2) a user
/// program that declares its own top-level `fun map(...)` (or `filter`/
/// `reduce`/`sort`) collides with the prelude's own declaration exactly
/// like redeclaring `print` already does -- a compile-time "already
/// declared" error, not a silent shadow.
///
/// Call convention (confirmed empirically against the real parser, not
/// just by reading its grammar): a callee immediately followed by `(`
/// with a comma inside -- `fn(a, b)` -- is *not* a 2-argument call. It's
/// a 1-argument call whose single argument is the parenthesized
/// comma-operator expression `(a, b)` (`Parser.fs`'s comma-as-operator
/// default, only ever suppressed while parsing a call's own *unparenthesized*
/// argument list). Every multi-argument call below is written the
/// no-parens way instead (`fn a, b`), matching `push v, x`'s own existing
/// surface syntax.
module Iqalox.Prelude

let source =
    """
fun map(fn, v) {
    var result = []
    for (var i mut = 0; i < length(v); ++i) {
        push result, fn(v[i])
    }
    return result
}

fun filter(fn, v) {
    var result = []
    for (var i mut = 0; i < length(v); ++i) {
        fn(v[i]) ? push result, v[i] : nil
    }
    return result
}

fun reduce(fn, v, initial) {
    var acc mut = initial
    for (var i mut = 0; i < length(v); ++i) {
        acc = fn acc, v[i]
    }
    return acc
}

fun sort(fn, v) {
    var result = [...v]
    var n = length(result)
    for (var i mut = 1; i < n; ++i) {
        var key = result[i]
        var j mut = i - 1
        for (; j >= 0 and fn key, result[j]; --j) {
            result[j + 1] = result[j]
        }
        result[j + 1] = key
    }
    return result
}
"""

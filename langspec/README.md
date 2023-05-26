# Iqalox Proof-of-Concept

The first iteration of **Iqalox** development. The overarching intention is to address
the most glaring features omitted in the educational toy language that is *Lox*, well
as steer it more into a direction more ameniable to the author's personal preferences.

## New features ##
- support for arrays
- support for maps and sets
- block comments
- implicit semicolons
- `continue` and `break` statements
- prefix increment and decrement operators (`++` & `--`)
- pipe operator (`|>`)
- ignore operator (`_`)
- anonymous functions (lambdas/closures)
- nullable infix operator (`??`)
- mixin support, e.g. `class A extends B with C` or `A with B`
- variadic unpacking (`...` operator)
- trait support, e.g. `trait A {...}` => `class B { use A }`
- comma operator

## Standard library features
- array manipulation
- support for standard library extensibility

## Breaking changes ##
- use of `extends` instead of `>` to indicate inheritance
- chainable ternary operator (`? :`) instead of `if else` control structure
- disallow string concat with the `+` operator, and use array manipulation standard library methods instead

_Note that some additional ideas presented inside the archived readmes may be considered, too_
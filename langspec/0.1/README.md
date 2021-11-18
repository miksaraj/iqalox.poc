# Iqalox v0.1 #

The first iteration of **Iqalox** development. The overarching intetion
is to address the most glaring features omitted in the educational toy
language that is *Lox*.

## New features: ##
- [ ] support for arrays
- [ ] support for hashmaps / associative arrays (whatever you want to call them...)
- [ ] block comments
- [ ] implicit semicolons
- [ ] `continue` and `break` statements
- [ ] prefix increment and decrement operators (`++` & `--`)

## Other TODOs: ##
- [ ] reserve keywords `with`, `concat`, `module` and `trait` 

## Breaking changes: ##
- [ ] accessing uninitialised variables as runtime error (implicit `undef` instead of `nil`)*


`*` The intention is to have a valid `nil` value for the use cases where it makes sense, but that the user will have to *explicitly* assign it to a variable. In order to be accessed *something* will have to be assigned to it:

```
var a       // allowed

var b = a   // runtime error

a = nil     // allowed

var c = a   // c == nil

var d

d = d + c   // runtime error, since d is undef and can't be accessed
```

I don't know if this makes sense, but I'll give it a shot...

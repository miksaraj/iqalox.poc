/// Resolver: `Stmt` list -> `BoundStmt` list. The compile-time
/// lexical-scope/variable-slot pass `docs/PLAN-0.1.md`'s Phase 4 calls
/// for -- locals get compile-time-known stack slots instead of runtime
/// hashmap lookups, closures get upvalues, and this is where compile-time
/// immutability enforcement and self-referencing-class-name binding both
/// live, per that plan.
///
/// The scope/slot/upvalue algorithm (`findLocal`/`resolveUpvalue`/
/// `addUpvalue`, and the "bump scope depth by one before declaring
/// parameters" trick that keeps a function's own parameters from being
/// mistaken for globals) is `clox`'s, from *Crafting Interpreters* Part
/// III -- `poc` has no resolver at all to port from for this phase, so
/// this is new, not a port.
///
/// `self`/`super` are resolved through the exact same local/upvalue/
/// global machinery as any other name, by looking up the synthetic names
/// "self"/"super" -- a method's function context gets an implicit `self`
/// local at slot 0, and a class with a superclass gets a synthetic
/// `super` local in a scope wrapping all of its methods (mirroring `clox`
/// again, and mirroring `poc`'s own extra-`Environment`-layer trick for
/// `super`, just done at compile time instead of at class-declaration
/// time). Falling through all the way to an unresolved global for either
/// name is exactly the "used outside a method" / "no superclass" error
/// case, since neither name can ever be a real user-declared identifier
/// (both are reserved keywords -- `var self = 1` doesn't parse).
///
/// Global declaration order doesn't matter for compile-time immutability
/// checking: top-level `var`/`fun`/`class` names are pre-registered in a
/// first pass (`preRegisterGlobals`) before the real resolution pass
/// walks any bodies, so a function that references a global declared
/// later in the same file (ordinary, expected mutual-recursion style code)
/// still gets a fully-informed immutability check, not a guess. Nested
/// (non-top-level) declarations have no such ambiguity to begin with --
/// they must be declared before use in their own scope, same as `poc`'s
/// dynamic `Environment` already requires.
module Iqalox.Resolver

open System.Collections.Generic
open Iqalox.Token
open Iqalox.Ast
open Iqalox.Bound

type ResolveError = { Message: string; Token: Token }

type private LocalVar = { Name: string; Slot: int; IsMutable: bool; Depth: int }
type private UpvalueEntry = { Name: string; Descriptor: UpvalueDescriptor; IsMutable: bool }

type private FunctionContext(enclosing: FunctionContext option) =
    member val ScopeDepth = 0 with get, set
    member val Locals = ResizeArray<LocalVar>() with get
    member val Upvalues = ResizeArray<UpvalueEntry>() with get
    member _.Enclosing = enclosing

let private findLocal (context: FunctionContext) (name: string) : LocalVar option =
    let mutable result = None
    let mutable i = context.Locals.Count - 1
    while result.IsNone && i >= 0 do
        if context.Locals.[i].Name = name then
            result <- Some context.Locals.[i]
        i <- i - 1
    result

let rec private addUpvalue (context: FunctionContext) (name: string) (descriptor: UpvalueDescriptor) (isMutable: bool) : int =
    match context.Upvalues |> Seq.tryFindIndex (fun u -> u.Descriptor = descriptor) with
    | Some i -> i
    | None ->
        context.Upvalues.Add { Name = name; Descriptor = descriptor; IsMutable = isMutable }
        context.Upvalues.Count - 1

and private resolveUpvalue (context: FunctionContext) (name: string) : (int * bool) option =
    match context.Enclosing with
    | None -> None
    | Some enclosing ->
        match findLocal enclosing name with
        | Some local ->
            let index = addUpvalue context name { FromEnclosingLocal = true; Index = local.Slot } local.IsMutable
            Some(index, local.IsMutable)
        | None ->
            match resolveUpvalue enclosing name with
            | Some(enclosingUpvalueIndex, isMutable) ->
                let index = addUpvalue context name { FromEnclosingLocal = false; Index = enclosingUpvalueIndex } isMutable
                Some(index, isMutable)
            | None -> None

/// Pre-registers every top-level `var`/`fun`/`class` name (with its
/// mutability) before any body is resolved, so a compile-time
/// immutability check against a global doesn't depend on where in the
/// file that global happens to be declared. Also where top-level
/// redeclaration is caught (`var x = 1 ... var x = 2`, matching `poc`'s
/// `Environment.define`'s "already declared" rule, just moved to compile
/// time here).
let private preRegisterGlobals (globals: Dictionary<string, bool>) (errors: ResizeArray<ResolveError>) (stmts: Stmt list) =
    let register (name: Token) (isMutable: bool) =
        if globals.ContainsKey name.Lexeme then
            errors.Add { Message = $"Variable '{name.Lexeme}' already declared."; Token = name }
        else
            globals.[name.Lexeme] <- isMutable

    for stmt in stmts do
        match stmt with
        | VarStmt(name, _, isMutable) -> register name isMutable
        | FunctionStmt decl -> register decl.Name false
        | ClassStmt(name, _, _, _, _, _) -> register name false
        | _ -> ()

/// A class or trait's own property/method declarations, flattened into
/// name-keyed maps -- the unit `checkNoConflicts`/`overrideWith`/
/// `mergeAll` below operate on, for `docs/PLAN-0.2.md` decision 12's
/// compile-time composition (traits via `use`, mixins via `with`).
type private MemberSet = { Properties: Map<string, PropertyDecl>; Methods: Map<string, FunctionDecl> }

let private emptyMemberSet: MemberSet = { Properties = Map.empty; Methods = Map.empty }

let private memberNames (m: MemberSet) : Set<string> =
    Set.union (m.Properties |> Map.toSeq |> Seq.map fst |> Set.ofSeq) (m.Methods |> Map.toSeq |> Seq.map fst |> Set.ofSeq)

/// `winner`'s own declarations take priority over anything `loser`
/// contributes under the same name -- used for "a class's/trait's own
/// body always overrides whatever composition brings in," never itself a
/// conflict (only *sibling* composed sources conflicting with each other
/// is, per `checkNoConflicts`).
let private overrideWith (winner: MemberSet) (loser: MemberSet) : MemberSet =
    { Properties = Map.fold (fun acc k v -> Map.add k v acc) loser.Properties winner.Properties
      Methods = Map.fold (fun acc k v -> Map.add k v acc) loser.Methods winner.Methods }

let private mergeAll (sets: MemberSet list) : MemberSet = sets |> List.fold overrideWith emptyMemberSet

/// docs/PLAN-0.2.md open questions 1-2, both resolved the same way by the
/// repository owner: two composed sources (used traits, a used trait vs.
/// the class's own superclass, `with`-mixins, a mixin vs. the superclass,
/// ...) sharing a member name is a compile-time error -- there is no
/// `insteadof`/`as`-style resolution syntax (not in `langspec/
/// SYNTAX_GRAMMAR.md`), so the only way out is renaming, or declaring the
/// member directly on the composing class/trait itself (which always
/// wins, silently, via `overrideWith` -- never itself flagged here).
let private checkNoConflicts
    (errors: ResizeArray<ResolveError>)
    (anchor: Token)
    (contextDescription: string)
    (sources: (string * MemberSet) list)
    : unit =
    let rec loop =
        function
        | [] -> ()
        | (name1: string, m1: MemberSet) :: rest ->
            for name2, m2 in rest do
                for overlapping in Set.intersect (memberNames m1) (memberNames m2) do
                    errors.Add
                        { Message =
                            $"'{overlapping}' is declared by both '{name1}' and '{name2}' in {contextDescription} -- rename one, or declare '{overlapping}' directly to resolve the conflict."
                          Token = anchor }
            loop rest
    loop sources

/// A single class or trait body's own declarations as a `MemberSet` --
/// also where a duplicate property, duplicate method, or a property/
/// method name collision *within that one body* is caught (unrelated to
/// `checkNoConflicts`, which is about *separate* composed sources
/// conflicting with each other, not one body's own internal duplicates).
let private buildOwnMemberSet
    (errors: ResizeArray<ResolveError>)
    (contextDescription: string)
    (properties: PropertyDecl list)
    (methods: FunctionDecl list)
    : MemberSet =
    let methodMap =
        methods
        |> List.fold
            (fun (acc: Map<string, FunctionDecl>) (m: FunctionDecl) ->
                if acc.ContainsKey m.Name.Lexeme then
                    errors.Add
                        { Message = $"Method '{m.Name.Lexeme}' already declared in {contextDescription}."
                          Token = m.Name }
                    acc
                else
                    acc.Add(m.Name.Lexeme, m))
            Map.empty
    let propMap =
        properties
        |> List.fold
            (fun (acc: Map<string, PropertyDecl>) (p: PropertyDecl) ->
                if acc.ContainsKey p.Name.Lexeme then
                    errors.Add
                        { Message = $"Property '{p.Name.Lexeme}' already declared in {contextDescription}."
                          Token = p.Name }
                    acc
                elif methodMap.ContainsKey p.Name.Lexeme then
                    errors.Add
                        { Message = $"'{p.Name.Lexeme}' is declared as both a property and a method in {contextDescription}."
                          Token = p.Name }
                    acc
                else
                    acc.Add(p.Name.Lexeme, p))
            Map.empty
    { Properties = propMap; Methods = methodMap }

/// One top-level `trait T { ... }`'s own raw declarations, keyed by name
/// -- built by `preRegisterTraits` before any class's `use` is resolved,
/// exactly like `preRegisterClasses` does for classes. A trait has no
/// `extends`/`with` of its own (`langspec/SYNTAX_GRAMMAR.md`'s
/// `traitDecl` grammar), only possibly-nested `use` of other traits.
type private TraitInfo =
    { Name: Token
      Properties: PropertyDecl list
      Methods: FunctionDecl list
      UsedTraitNames: Token list }

let private preRegisterTraits (traits: Dictionary<string, TraitInfo>) (errors: ResizeArray<ResolveError>) (stmts: Stmt list) =
    for stmt in stmts do
        match stmt with
        | TraitStmt(name, properties, methods, usedTraits) ->
            if traits.ContainsKey name.Lexeme then
                errors.Add { Message = $"Trait '{name.Lexeme}' already declared."; Token = name }
            else
                traits.[name.Lexeme] <-
                    { Name = name; Properties = properties; Methods = methods; UsedTraitNames = usedTraits }
        | _ -> ()

/// Recursively flattens trait `traitName`'s own members plus everything
/// its own nested `use` clauses pull in, applying `checkNoConflicts` at
/// every level (two of a trait's own nested-used traits conflicting is
/// exactly as much an error as two of a *class*'s used traits
/// conflicting -- same rule, just one level higher). `visiting` turns a
/// `trait A { use B }` / `trait B { use A }` cycle into a clean error
/// instead of unbounded recursion; `cache` means a diamond (two different
/// traits both using a third) only flattens that third trait once.
let rec private flattenTrait
    (traits: Dictionary<string, TraitInfo>)
    (errors: ResizeArray<ResolveError>)
    (cache: Dictionary<string, MemberSet>)
    (visiting: Set<string>)
    (usageToken: Token)
    (traitName: string)
    : MemberSet =
    match cache.TryGetValue traitName with
    | true, cached -> cached
    | false, _ ->
        if visiting.Contains traitName then
            errors.Add { Message = $"Circular trait composition involving '{traitName}'."; Token = usageToken }
            emptyMemberSet
        else
            match traits.TryGetValue traitName with
            | false, _ ->
                errors.Add { Message = $"Undefined trait '{traitName}'."; Token = usageToken }
                emptyMemberSet
            | true, info ->
                let visiting = visiting.Add traitName
                let nested =
                    info.UsedTraitNames
                    |> List.map (fun t -> t.Lexeme, flattenTrait traits errors cache visiting t t.Lexeme)
                checkNoConflicts errors info.Name $"trait '{traitName}'" nested
                let ownSet = buildOwnMemberSet errors $"trait '{traitName}'" info.Properties info.Methods
                let result = overrideWith ownSet (nested |> List.map snd |> mergeAll)
                cache.[traitName] <- result
                result

/// One top-level class's own declared members, keyed by name -- built by
/// `preRegisterClasses` before any method body is resolved, exactly like
/// `preRegisterGlobals` above, so forward/mutual references between
/// classes (and looking up an ancestor's/mixin's declared properties
/// while resolving a *subclass*'s methods) don't depend on file order.
/// `docs/PLAN-0.2.md` decision 8's addendum: every property a class uses
/// must be declared somewhere in its own body, an ancestor's, a `with`
/// mixin's, or a `use`d trait's -- this is the compile-time table that
/// check is answered against. `InlinedProperties`/`InlinedMethods`
/// already have every `use`d trait's own members merged in (per
/// `flattenTrait` above) -- `Resolver.fs`'s `ClassStmt` case resolves
/// these two lists directly and needs no further `use` handling of its
/// own at all; only `SuperclassName`/`MixinNames` remain to walk for
/// `effectiveClassMembers` below, since those two compose at *runtime*
/// (`Inherit`/`Mixin`), not at compile time.
type private ClassInfo =
    { Name: Token
      SuperclassName: string option
      MixinNames: string list
      /// The class's own literal body only -- never conflict-checked
      /// against a used trait (a class's own explicit declaration always
      /// silently overrides a trait's, matching PHP's real semantics),
      /// but *does* still need checking against the superclass/mixins in
      /// `effectiveClassMembers` (a property redeclaration is still an
      /// error there; a method override is not).
      OwnLiteral: MemberSet
      /// Everything brought in via `use`, already flattened/conflict-
      /// checked against `OwnLiteral`'s siblings (other used traits) --
      /// kept separate from `OwnLiteral` because a trait-contributed
      /// member conflicting with the superclass/a mixin *is* one of
      /// open question 1's named conflict scenarios ("a trait and the
      /// class's own superclass"), unlike the class's own literal
      /// declarations, which always win silently over anything composed.
      TraitContributed: MemberSet
      /// `OwnLiteral` merged over `TraitContributed` -- exactly what
      /// `Resolver.fs`'s `ClassStmt` case resolves as this class's own
      /// properties/methods, needing no further `use` handling at all.
      InlinedProperties: PropertyDecl list
      InlinedMethods: FunctionDecl list }

let private preRegisterClasses
    (classes: Dictionary<string, ClassInfo>)
    (traits: Dictionary<string, TraitInfo>)
    (traitCache: Dictionary<string, MemberSet>)
    (errors: ResizeArray<ResolveError>)
    (stmts: Stmt list)
    =
    for stmt in stmts do
        match stmt with
        | ClassStmt(name, superclassExpr, mixinExprs, properties, methods, usedTraits) ->
            let superclassName =
                superclassExpr
                |> Option.map (function
                    | Variable superName -> superName.Lexeme
                    | other -> failwith $"unreachable: superclass is always a bare Variable, got %A{other}")
            let mixinNames =
                mixinExprs
                |> List.map (function
                    | Variable mixinName -> mixinName.Lexeme
                    | other -> failwith $"unreachable: a mixin is always a bare Variable, got %A{other}")

            let ownLiteral = buildOwnMemberSet errors $"class '{name.Lexeme}'" properties methods

            // No `use` at all is by far the common case (every class
            // before this phase existed) -- skip the `Map`-keyed merge
            // entirely then, so a plain class's property/method order in
            // the emitted bytecode is *exactly* its own declaration
            // order, unchanged from before `use`/`with` existed. Only a
            // class that actually composes traits pays for (and needs)
            // the conflict-checked merge below, whose result is ordered
            // by name (`Map.toList`) rather than by declaration site --
            // a reasonable, deterministic tradeoff for genuinely new
            // functionality with no prior ordering to preserve.
            let traitContributed, inlinedProperties, inlinedMethods =
                if usedTraits.IsEmpty then
                    emptyMemberSet, properties, methods
                else
                    let usedTraitSets =
                        usedTraits
                        |> List.map (fun t -> t.Lexeme, flattenTrait traits errors traitCache Set.empty t t.Lexeme)
                    checkNoConflicts errors name $"class '{name.Lexeme}'" usedTraitSets
                    let traitContributed = usedTraitSets |> List.map snd |> mergeAll
                    let effective = overrideWith ownLiteral traitContributed
                    traitContributed,
                    (effective.Properties |> Map.toList |> List.map snd),
                    (effective.Methods |> Map.toList |> List.map snd)

            classes.[name.Lexeme] <-
                { Name = name
                  SuperclassName = superclassName
                  MixinNames = mixinNames
                  OwnLiteral = ownLiteral
                  TraitContributed = traitContributed
                  InlinedProperties = inlinedProperties
                  InlinedMethods = inlinedMethods }
        | _ -> ()

/// Recursively computes class `className`'s full effective member set --
/// its own body plus its used traits (already flattened into
/// `InlinedProperties`/`InlinedMethods`), plus its superclass's and every
/// `with`-mixin's own effective sets (each resolved the same way,
/// transitively). `checkNoConflicts` applies to the superclass and every
/// mixin as siblings -- decision 12's "closer to real multiple
/// inheritance" without full C3 (docs/PLAN-0.2.md open question 2,
/// deferred) still needs *some* answer for "what if two mixins (or a
/// mixin and the superclass) disagree," and the repository owner's Q1
/// answer (compile error on any trait conflict) is applied symmetrically
/// here rather than picking a silent copy-order winner for mixins alone.
/// The class's own body (already folded into `InlinedProperties`/
/// `InlinedMethods`) always overrides any of this, same as `use`.
let rec private effectiveClassMembers
    (classes: Dictionary<string, ClassInfo>)
    (errors: ResizeArray<ResolveError>)
    (cache: Dictionary<string, MemberSet>)
    (visiting: Set<string>)
    (usageToken: Token)
    (className: string)
    : MemberSet =
    match cache.TryGetValue className with
    | true, cached -> cached
    | false, _ ->
        if visiting.Contains className then
            errors.Add { Message = $"Circular class hierarchy involving '{className}'."; Token = usageToken }
            emptyMemberSet
        else
            match classes.TryGetValue className with
            | false, _ -> emptyMemberSet // an undeclared name here is caught elsewhere (Inherit/Mixin's own runtime "must be a class" check)
            | true, info ->
                let visiting = visiting.Add className
                let superSet =
                    info.SuperclassName
                    |> Option.map (fun s -> s, effectiveClassMembers classes errors cache visiting info.Name s)
                let mixinSets =
                    info.MixinNames |> List.map (fun m -> m, effectiveClassMembers classes errors cache visiting info.Name m)
                let composedSources = (superSet |> Option.toList) @ mixinSets
                checkNoConflicts errors info.Name $"class '{className}'" composedSources
                let composedMerged = composedSources |> List.map snd |> mergeAll

                // `TraitContributed` is checked against the composed
                // (superclass/mixin) set exactly like any other pair of
                // composed sources conflicting -- open question 1 names
                // "a trait and the class's own superclass" as one of its
                // own conflict scenarios, so a trait-provided member is
                // never allowed to silently win over (or lose to) an
                // ancestor/mixin, unlike the class's own literal body.
                checkNoConflicts errors info.Name $"class '{className}'" [ $"class '{className}' (used traits)", info.TraitContributed; "its ancestors/mixins", composedMerged ]

                // The class's own *literal* declarations always win --
                // silently for methods (ordinary polymorphism, unchanged
                // since 0.1), but a redeclared *property* still has no
                // override semantics to speak of (decision 8/9's
                // write-once model has no notion of "which declaration
                // wins"), so it stays the compile-time error
                // `docs/PLAN-0.2.md` Phase 7 originally established for
                // the superclass-only case, now applied to mixins too for
                // the same reason.
                let inheritedMerged = overrideWith info.TraitContributed composedMerged
                for propName in Set.intersect (info.OwnLiteral.Properties |> Map.toSeq |> Seq.map fst |> Set.ofSeq) (memberNames inheritedMerged) do
                    errors.Add
                        { Message = $"Property '{propName}' already declared by an ancestor/mixin of class '{className}'."
                          Token = info.Name }
                let result = overrideWith info.OwnLiteral inheritedMerged
                cache.[className] <- result
                result

/// Runs `effectiveClassMembers` for every registered class once, so every
/// conflict is reported exactly once (subsequent lookups from
/// `findDeclaredProperty`/`findDeclaredMethod` during the main resolve
/// pass just hit this cache, never re-triggering `checkNoConflicts`).
let private computeAllEffectiveClassMembers
    (classes: Dictionary<string, ClassInfo>)
    (errors: ResizeArray<ResolveError>)
    : Dictionary<string, MemberSet> =
    let cache = Dictionary<string, MemberSet>()
    for KeyValue(className, info) in classes do
        effectiveClassMembers classes errors cache Set.empty info.Name className |> ignore
    cache

/// Looks `propName` up in `className`'s full effective member set (own
/// body, used traits, superclass, and mixins, all already flattened by
/// `computeAllEffectiveClassMembers`) -- mirrors decision 10's internal-
/// access rule ("self.x" resolves against whichever source in the
/// composition actually declared `x`), per the now-resolved §2.3
/// protected-like reading extended to mixins/traits the same way.
let private findDeclaredProperty
    (effectiveMembers: Dictionary<string, MemberSet>)
    (className: string)
    (propName: string)
    : PropertyDecl option =
    match effectiveMembers.TryGetValue className with
    | true, m -> m.Properties.TryFind propName
    | false, _ -> None

/// Same lookup, for method names -- used only to give a more specific
/// "that's a method, not a property" diagnostic when a `self.x = value`
/// targets a declared method instead of an undeclared name entirely
/// (decision 8's addendum doesn't cover this case explicitly; a
/// dedicated message is just better engineering, not a new design axis).
let private findDeclaredMethod (effectiveMembers: Dictionary<string, MemberSet>) (className: string) (methodName: string) : bool =
    match effectiveMembers.TryGetValue className with
    | true, m -> m.Methods.ContainsKey methodName
    | false, _ -> false

type private Resolver(globals: Dictionary<string, bool>, classes: Dictionary<string, ClassInfo>, effectiveMembers: Dictionary<string, MemberSet>) =
    let errors = ResizeArray<ResolveError>()
    let mutable context = FunctionContext(None)
    // The name of the class whose method body is currently being resolved
    // -- `None` outside any method. Used only by `Set(SelfExpr, ...)`'s
    // decision-8-addendum check (`self.x = value` must target a property
    // declared somewhere in this class's own hierarchy); reading `self.x`
    // needs no such check (`Get` is ambiguous between a property read and
    // a bound-method fetch, so it's left to the existing runtime
    // "Undefined property" fallback, same as before this phase).
    let mutable currentClassName: string option = None

    let error (token: Token) (message: string) = errors.Add { Message = message; Token = token }

    let beginScope () = context.ScopeDepth <- context.ScopeDepth + 1

    let endScope () =
        context.ScopeDepth <- context.ScopeDepth - 1
        while context.Locals.Count > 0 && context.Locals.[context.Locals.Count - 1].Depth > context.ScopeDepth do
            context.Locals.RemoveAt(context.Locals.Count - 1)

    let resolveReference (name: string) : VariableBinding * bool option =
        match findLocal context name with
        | Some local -> LocalBinding local.Slot, Some local.IsMutable
        | None ->
            match resolveUpvalue context name with
            | Some(index, isMutable) -> UpvalueBinding index, Some isMutable
            | None ->
                match globals.TryGetValue name with
                | true, isMutable -> GlobalBinding name, Some isMutable
                | false, _ -> GlobalBinding name, None

    let declareVariable (name: Token) (isMutable: bool) : DeclaredBinding =
        if context.ScopeDepth = 0 then
            // Pre-registered by preRegisterGlobals already -- nothing left
            // to check or insert here.
            DeclaredGlobal name.Lexeme
        else
            let alreadyInScope =
                context.Locals |> Seq.exists (fun l -> l.Depth = context.ScopeDepth && l.Name = name.Lexeme)
            if alreadyInScope then
                error name $"Variable '{name.Lexeme}' already declared in this scope."
            let slot = context.Locals.Count
            context.Locals.Add { Name = name.Lexeme; Slot = slot; IsMutable = isMutable; Depth = context.ScopeDepth }
            DeclaredLocal slot

    member this.Errors = List.ofSeq errors

    member this.ResolveStmt(stmt: Stmt) : BoundStmt =
        match stmt with
        | Block statements -> BBlock(this.ResolveBlock statements)
        | ExpressionStmt expr -> BExpressionStmt(this.ResolveExpr expr)
        | VarStmt(name, initializer, isMutable) ->
            // Resolve the initializer *before* declaring the name -- `var
            // x = x` must see whatever `x` (if any) already existed in an
            // enclosing scope, matching poc's Environment-based semantics
            // (the initializer is evaluated before `environment.define`
            // runs).
            let boundInit = initializer |> Option.map this.ResolveExpr
            let binding = declareVariable name isMutable
            BVarStmt(binding, name, boundInit)
        | ForStmt(initializer, condition, increment, body) ->
            beginScope ()
            let boundInit = initializer |> Option.map this.ResolveStmt
            let boundCond = condition |> Option.map this.ResolveExpr
            let boundIncr = increment |> Option.map this.ResolveExpr
            let boundBody = this.ResolveStmt body
            endScope ()
            BForStmt(boundInit, boundCond, boundIncr, boundBody)
        | FunctionStmt decl ->
            // The function's own name is declared before its body is
            // resolved (unlike VarStmt) so recursive calls to itself
            // resolve correctly.
            let binding = declareVariable decl.Name false
            BFunctionStmt(binding, this.ResolveFunction(decl, isMethod = false))
        | ReturnStmt(keyword, value) -> BReturnStmt(keyword, value |> Option.map this.ResolveExpr)
        | ClassStmt(name, superclassExpr, mixinExprs, properties, methods, _) ->
            let binding = declareVariable name false

            let boundSuperclass =
                superclassExpr
                |> Option.map (function
                    | Variable superName ->
                        let refBinding, _ = resolveReference superName.Lexeme
                        refBinding, superName
                    | other -> failwith $"unreachable: superclass is always a bare Variable, got %A{other}")

            let boundMixins: (VariableBinding * Token) list =
                mixinExprs
                |> List.map (function
                    | Variable mixinName ->
                        let refBinding, _ = resolveReference mixinName.Lexeme
                        refBinding, mixinName
                    | other -> failwith $"unreachable: a mixin is always a bare Variable, got %A{other}")

            if boundSuperclass.IsSome then
                beginScope ()
                // A synthetic `super` local, resolvable (as an upvalue)
                // from every method that references it -- see the module
                // doc comment.
                context.Locals.Add
                    { Name = "super"
                      Slot = context.Locals.Count
                      IsMutable = false
                      Depth = context.ScopeDepth }

            // `classes.[name.Lexeme]`'s Inlined*/* already has this
            // class's own body merged with every `use`d trait's members
            // (`preRegisterClasses`/`flattenTrait`) -- resolved here
            // exactly as if the user had written them directly, so a
            // trait method's `self`/`super` naturally resolve relative to
            // *this* class, matching PHP's own trait semantics (a trait
            // has no inheritance identity of its own). `preRegisterClasses`
            // only walks *top-level* statements (mirroring
            // `preRegisterGlobals`'s own pre-existing scoping limit), so a
            // nested class -- already an edge case nothing exercises --
            // falls back to its own raw, un-inlined `properties`/`methods`
            // instead of crashing; it simply doesn't get `use`/`with`
            // composition, exactly like it didn't get decision 8's
            // declared-property checking before this phase either.
            let inlinedProperties, inlinedMethods =
                match classes.TryGetValue name.Lexeme with
                | true, info -> info.InlinedProperties, info.InlinedMethods
                | false, _ -> properties, methods
            let enclosingClassName = currentClassName
            currentClassName <- Some name.Lexeme
            let boundMethods = inlinedMethods |> List.map (fun m -> this.ResolveFunction(m, isMethod = true))
            currentClassName <- enclosingClassName

            if boundSuperclass.IsSome then
                endScope ()

            let boundProperties: BoundPropertyDecl list =
                inlinedProperties
                |> List.map (fun p -> ({ Name = p.Name; IsPub = p.IsPub; IsMutable = p.IsMutable }: BoundPropertyDecl))

            BClassStmt(binding, name, boundSuperclass, boundMixins, boundProperties, boundMethods)
        | TraitStmt(name, _, _, _) ->
            // `resolve` filters every top-level `TraitStmt` out before
            // this ever runs (traits are pre-consumed entirely at compile
            // time -- see the module doc comment on `Ast.TraitStmt`). A
            // *nested* trait declaration (inside a function/block) isn't
            // pre-registered at all, mirroring the same top-level-only
            // scoping limit `preRegisterClasses`/`preRegisterGlobals`
            // already have for nested classes/globals -- reported as a
            // clear compile error here rather than silently doing nothing
            // or crashing.
            error name "Trait declarations are only supported at the top level."
            BBlock []

    member private this.ResolveBlock(statements: Stmt list) : BoundStmt list =
        beginScope ()
        let bound = statements |> List.map this.ResolveStmt
        endScope ()
        bound

    member private this.ResolveFunction(decl: FunctionDecl, isMethod: bool) : BoundFunctionDecl =
        let enclosing = context
        context <- FunctionContext(Some enclosing)
        // Bump depth before declaring anything, so the function's own
        // parameters and top-level body statements are never mistaken
        // for globals (depth 0 means "the script's own top level," full
        // stop -- see the module doc comment).
        beginScope ()

        if isMethod then
            context.Locals.Add { Name = "self"; Slot = 0; IsMutable = false; Depth = context.ScopeDepth }

        // Parameters are always immutable locals, matching poc (no
        // grammar exists for an individual mutable parameter).
        for parameter in decl.Parameters do
            declareVariable parameter false |> ignore

        let boundBody = decl.Body |> List.map this.ResolveStmt

        let result =
            { Name = decl.Name
              Parameters = decl.Parameters
              Body = boundBody
              LocalCount = context.Locals.Count
              Upvalues = context.Upvalues |> Seq.map (fun u -> u.Descriptor) |> List.ofSeq
              IsPub = decl.IsPub }

        context <- enclosing
        result

    member private this.ResolveExpr(expr: Expr) : BoundExpr =
        match expr with
        | Assign(name, value) ->
            let boundValue = this.ResolveExpr value
            let binding, isMutable = resolveReference name.Lexeme
            match isMutable with
            | Some false -> error name $"Assigning to immutable variable '{name.Lexeme}' not allowed."
            | Some true -> ()
            | None -> () // an as-yet-unresolved global -- deferred to runtime, same as poc today
            BAssign(binding, name, boundValue)
        | Binary(left, operator, right) -> BBinary(this.ResolveExpr left, operator, this.ResolveExpr right)
        | Logical(left, operator, right) -> BLogical(this.ResolveExpr left, operator, this.ResolveExpr right)
        | Grouping inner -> BGrouping(this.ResolveExpr inner)
        | Literal value -> BLiteral value
        | Unary(operator, right) -> BUnary(operator, this.ResolveExpr right)
        | Ternary(left, leftOp, middle, rightOp, right) ->
            BTernary(this.ResolveExpr left, leftOp, this.ResolveExpr middle, rightOp, this.ResolveExpr right)
        | Vector values -> BVector(values |> List.map this.ResolveExpr)
        | Spread(expr, ellipsis) -> BSpread(this.ResolveExpr expr, ellipsis)
        | Variable name ->
            let binding, _ = resolveReference name.Lexeme
            BVariable(binding, name)
        | BreakExpr keyword -> BBreak keyword
        | ContinueExpr keyword -> BContinue keyword
        | Ignore -> BIgnore
        | Call(callee, arguments) -> BCall(this.ResolveExpr callee, arguments |> List.map this.ResolveExpr)
        | Get(obj, name) -> BGet(this.ResolveExpr obj, name)
        | Set(obj, name, value) ->
            // `docs/PLAN-0.2.md` decision 8's addendum: a bare
            // `self.x = value` with no matching property declaration
            // anywhere in the current class's own hierarchy is a
            // compile-time error, the same category as assigning to an
            // undeclared local -- only checkable here (not for an
            // external `instance.x = value`, whose `instance` expression
            // has no statically-known class to check against).
            match obj, currentClassName with
            | SelfExpr _, Some className when (findDeclaredProperty effectiveMembers className name.Lexeme).IsNone ->
                if findDeclaredMethod effectiveMembers className name.Lexeme then
                    error name $"'{name.Lexeme}' is a method, not a property -- it can't be assigned to."
                else
                    error name $"Property '{name.Lexeme}' is not declared in class '{className}' or any of its superclasses/mixins/traits."
            | _ -> ()
            BSet(this.ResolveExpr obj, name, this.ResolveExpr value)
        | Index(obj, index, bracket) -> BIndex(this.ResolveExpr obj, this.ResolveExpr index, bracket)
        | IndexSet(obj, index, value, bracket) ->
            BIndexSet(this.ResolveExpr obj, this.ResolveExpr index, this.ResolveExpr value, bracket)
        | Slice(obj, start, stop, bracket) ->
            BSlice(this.ResolveExpr obj, start |> Option.map this.ResolveExpr, stop |> Option.map this.ResolveExpr, bracket)
        | Lambda(parameters, arrow, body) ->
            // Desugars to a nameless FunctionDecl with a single implicit
            // `return` statement, then resolves exactly like a nested
            // named function (`ResolveFunction`, unchanged) -- same
            // scope/slot/upvalue machinery, per docs/PLAN-0.2.md §3. The
            // synthetic `Name` token is only ever used for diagnostics/
            // disassembly (`Codegen.fs`'s `FunctionProto.Name`); nothing
            // binds a lambda to it.
            let syntheticDecl: FunctionDecl =
                { Name = { arrow with Type = Identifier; Lexeme = "lambda" }
                  Parameters = parameters
                  Body = [ ReturnStmt(arrow, Some body) ]
                  IsPub = false }
            BLambda(this.ResolveFunction(syntheticDecl, isMethod = false))
        | Cons(item, list, bracket) ->
            // `[item | list]` (docs/PLAN-0.2.md decision 2) needs a real
            // runtime loop -- `list`'s length isn't known at compile
            // time, so this can't be a fixed-operand `BuildVector`. A
            // first attempt allocated hidden local slots directly in the
            // *enclosing* scope for the accumulator/source/index, but
            // that's unsound: a `Cons` can appear anywhere an expression
            // can, including mid-expression (e.g. a call argument), where
            // other transient values the enclosing expression already
            // pushed (like the callee itself) sit above the slot numbers
            // `Resolver.fs`'s ordinary `declareVariable` would compute,
            // corrupting every `GetLocal`/`SetLocal` after it -- found by
            // `[1 | []]` failing as a bare call argument (`print [1 | []]`)
            // while working fine as a `var` initializer. Fixed by
            // desugaring to a call of a synthetic, isolated zero-context
            // closure instead: `item`/`list` are evaluated once in the
            // *enclosing* scope and passed in as ordinary call arguments,
            // and everything the closure's own body needs (`$result`,
            // `$index`) lives in its own fresh frame, entirely decoupled
            // from whatever the enclosing expression already has on the
            // stack -- exactly the same isolation a lambda already gets,
            // and exactly how `Return` already extracts a value out from
            // under a callee's own locals.
            let itemParam = { bracket with Type = Identifier; Lexeme = "$item" }
            let listParam = { bracket with Type = Identifier; Lexeme = "$list" }
            let resultName = { bracket with Type = Identifier; Lexeme = "$result" }
            let indexName = { bracket with Type = Identifier; Lexeme = "$index" }
            let lessOp = { bracket with Type = Less; Lexeme = "<" }
            let incrOp = { bracket with Type = PlusPlus; Lexeme = "++" }
            let syntheticDecl: FunctionDecl =
                { Name = { bracket with Type = Identifier; Lexeme = "cons" }
                  Parameters = [ itemParam; listParam ]
                  Body =
                    [ VarStmt(resultName, Some(Vector [ Variable itemParam ]), false)
                      VarStmt(indexName, Some(Literal(NumberValue 0.0)), true)
                      ForStmt(
                          None,
                          Some(Binary(Variable indexName, lessOp, InternalVectorLength(Variable listParam))),
                          Some(Unary(incrOp, Variable indexName)),
                          ExpressionStmt(
                              InternalVectorAppend(Variable resultName, Index(Variable listParam, Variable indexName, bracket))
                          )
                      )
                      ReturnStmt(bracket, Some(Variable resultName)) ]
                  IsPub = false }
            let boundDecl = this.ResolveFunction(syntheticDecl, isMethod = false)
            let boundItem = this.ResolveExpr item
            let boundList = this.ResolveExpr list
            BCall(BLambda boundDecl, [ boundItem; boundList ])
        | ListComprehension(body, variable, source, bracket) ->
            // Same runtime-length problem, same synthetic-closure fix as
            // `Cons` above -- `source` becomes the closure's sole
            // parameter (evaluated once, in the enclosing scope); the
            // user's own bound name (`variable`) is declared fresh each
            // iteration inside the loop body's own block scope (so its
            // slot is correctly reused iteration to iteration, exactly
            // like any other block-scoped `var` already is), and `body`
            // is resolved *within* the synthetic function so any
            // reference to an enclosing-scope name (not just `variable`)
            // correctly captures it as an upvalue, same as a lambda's
            // body would.
            let sourceParam = { bracket with Type = Identifier; Lexeme = "$source" }
            let resultName = { bracket with Type = Identifier; Lexeme = "$result" }
            let indexName = { bracket with Type = Identifier; Lexeme = "$index" }
            let lessOp = { bracket with Type = Less; Lexeme = "<" }
            let incrOp = { bracket with Type = PlusPlus; Lexeme = "++" }
            let syntheticDecl: FunctionDecl =
                { Name = { bracket with Type = Identifier; Lexeme = "comprehension" }
                  Parameters = [ sourceParam ]
                  Body =
                    [ VarStmt(resultName, Some(Vector []), false)
                      VarStmt(indexName, Some(Literal(NumberValue 0.0)), true)
                      ForStmt(
                          None,
                          Some(Binary(Variable indexName, lessOp, InternalVectorLength(Variable sourceParam))),
                          Some(Unary(incrOp, Variable indexName)),
                          Block [
                              VarStmt(variable, Some(Index(Variable sourceParam, Variable indexName, bracket)), false)
                              ExpressionStmt(InternalVectorAppend(Variable resultName, body))
                          ]
                      )
                      ReturnStmt(bracket, Some(Variable resultName)) ]
                  IsPub = false }
            let boundDecl = this.ResolveFunction(syntheticDecl, isMethod = false)
            let boundSource = this.ResolveExpr source
            BCall(BLambda boundDecl, [ boundSource ])
        | InternalVectorLength vector -> BVectorLengthInternal(this.ResolveExpr vector)
        | InternalVectorAppend(vector, value) -> BVectorAppendInternal(this.ResolveExpr vector, this.ResolveExpr value)
        | SelfExpr keyword ->
            match resolveReference "self" with
            | GlobalBinding _, None ->
                error keyword "Can't use 'self' outside of a method."
                BSelf(GlobalBinding "self", keyword)
            | binding, _ -> BSelf(binding, keyword)
        | SuperExpr(keyword, method) ->
            match resolveReference "super" with
            | GlobalBinding _, None ->
                error keyword "Can't use 'super' outside of a class with a superclass."
                BSuper(GlobalBinding "self", GlobalBinding "super", keyword, method)
            | binding, _ ->
                // Resolved independently of `super` itself (mirroring `clox`) so
                // that a `super.method()` call from inside a closure nested
                // *within* a method still finds the right `self` -- e.g. via an
                // upvalue chain of its own -- rather than assuming `self` is
                // always the current function's own slot 0.
                let selfBinding, _ = resolveReference "self"
                BSuper(selfBinding, binding, keyword, method)

/// Native functions the VM provides without any user declaration (Phase
/// 7: `print`, `concat`; Phase 5 of `docs/PLAN-0.2.md`: `push`, `pop`,
/// `length`, `reverse`; Phase 6: `transpose`, `multiply`, `add`,
/// `subtract`) -- pre-registered as immutable globals, exactly like a
/// user-declared `fun`, so `print = 5` is a compile-time immutability
/// error and `var print = 1` is a compile-time redeclaration error
/// instead of silently succeeding just because the resolver has never
/// heard of the name. Mirrors `poc`'s `Interpreter.__init__`, which
/// defines both in the same environment chain user code runs in, with
/// `is_mutable=False`, before any user statement executes. Keep this in
/// sync with `vm/src/vm.cpp`'s `defineNatives` -- the two toolchains have
/// no shared source of truth for this list. `map`/`filter`/`reduce`/`sort`/
/// `elementwise` are deliberately NOT here -- they're ordinary `fun`
/// declarations (`Prelude.fs`) resolved as part of the same program as
/// any user `fun`, needing no special-casing at all.
let private nativeGlobals =
    [ "print"; "concat"; "push"; "pop"; "length"; "reverse"; "transpose"; "multiply"; "add"; "subtract" ]

/// Resolves `stmts` into a `BoundStmt` list plus any resolution errors
/// (compile-time immutability violations, redeclarations, `self`/`super`
/// used where they can't resolve). Never throws -- every error is
/// recorded and resolution continues, since (unlike a syntax error) a
/// semantic error doesn't prevent understanding the rest of the program's
/// structure.
let resolve (stmts: Stmt list) : BoundStmt list * ResolveError list =
    let globals = Dictionary<string, bool>()
    for name in nativeGlobals do
        globals.[name] <- false
    let preErrors = ResizeArray<ResolveError>()
    preRegisterGlobals globals preErrors stmts

    let traits = Dictionary<string, TraitInfo>()
    preRegisterTraits traits preErrors stmts
    let traitCache = Dictionary<string, MemberSet>()

    let classes = Dictionary<string, ClassInfo>()
    preRegisterClasses classes traits traitCache preErrors stmts
    let effectiveMembers = computeAllEffectiveClassMembers classes preErrors

    // Every `TraitStmt` is fully consumed above (`preRegisterTraits`/
    // `flattenTrait`, inlined into whichever class(es) `use` it) and
    // never given a `BoundStmt` of its own -- filtered out here rather
    // than in `ResolveStmt`, which only exists to catch a *nested* trait
    // declaration (not pre-registered, see its own case) as a clean
    // error instead of silently vanishing.
    let nonTraitStmts = stmts |> List.filter (function TraitStmt _ -> false | _ -> true)

    let resolver = Resolver(globals, classes, effectiveMembers)
    let bound = nonTraitStmts |> List.map resolver.ResolveStmt

    bound, (List.ofSeq preErrors) @ resolver.Errors

# Diagnostic reference

Quick reference for all compile-time diagnostics emitted by the Blueprint source generator.
For detailed explanations, see [compile-time-validation.md](compile-time-validation.md).

## Core diagnostics (BP)

### Shape and registration (BP001–BP099)

| ID | Severity | Title |
|----|----------|-------|
| BP001 | Warning | Class should be partial for optimized Object property (API resource) |
| BP002 | Error | Class must be partial (app model) |
| BP004 | Error | Cannot parse `[BlueprintAppScan]` attribute |
| BP005 | Error | Class must be partial (descriptor) |
| BP006 | Error | Serializer context is missing required response body coverage |
| BP007 | Error | App is missing a serializer context declaration |
| BP008 | Error | Serializer-covered body type uses unsupported OpenAPI validation shape |
| BP009 | Error | Duplicate descriptor name once Query/Command is removed and the name is cased |
| BP010 | Warning | Non-evaluable ApiExceptionFactory initializer |
| BP011 | Error | Duplicate or empty descriptor name after the app's `[BlueprintAppOperationNames]` removal |
| BP012 | Error | Response body member is open-typed |
| BP013 | Error | Declared runtime type is not registered with the serializer context |

### BP012 — open-typed member reachable from a declared response root

A declared response root is written by the app's source-generated `JsonSerializerContext` alone. A member typed `object` (or a collection or dictionary of `object`) is written as whatever runtime type it holds, and a type the context does not register fails the response when it is written. An interface or abstract class without `[JsonPolymorphic]`/`[JsonDerivedType]` is written as the declared type, dropping the rest. Say what the member holds:

```csharp
// ❌ BP012 — a proxied JsonElement or a List<Error> fails the response when written
public Dictionary<string, object?> Details { get; set; }

// ✅ the listed types must be [JsonSerializable] on the app's context (BP013 otherwise)
[BlueprintRuntimeTypes(typeof(JsonElement), typeof(List<Error>))]
public Dictionary<string, object?> Details { get; set; }

// ✅ or a converter owns the member's shape
[JsonConverter(typeof(DetailsConverter))]
public Dictionary<string, object?> Details { get; set; }
```

Or opt the whole app into the runtime fallback, which writes unregistered types with reflection and logs each once (no trimming/AOT; see [compile-time-validation.md](compile-time-validation.md)):

```csharp
[BlueprintAppSerializerContext(JsonSerializerContextType = typeof(AppJsonContext), AllowRuntimeTypeFallback = true)]
```

`ProblemDetails.Extensions` and `ResourceEvent`'s `Data`/`Metadata`/`SecureData`/`ChangedValues` are not reported when the problem or event is itself the response root: it is written with fallback metadata at runtime. Nested inside another response type they are reported.

A member in a referenced assembly has no source location, so its diagnostic is reported on the app's `[BlueprintAppSerializerContext]` and names the member in its message.

### BP013 — declared runtime type is not registered

A type listed in `[BlueprintRuntimeTypes(...)]` is not registered on the app-owned context. Add `[JsonSerializable(typeof(...))]` for it.

### Pipeline and handler (BP100–BP199)

| ID | Severity | Title |
|----|----------|-------|
| BP100 | Error | No handler found for operation |
| BP101 | Error | Multiple handlers found for operation |
| BP104 | Error | Operation class must be partial |
| BP105 | Error | Nullable plain-value handler return is not supported |
| BP106 | Error | `Results<>` union member is not supported |
| BP107 | Error | Handler return type is incompatible with declared operation result contract |
| BP108 | Error | Declared semantic result has no enabled-host executor coverage |
| BP109 | Error | Declared semantic result union has ambiguous host translation |
| BP110 | Error | HTTP-visible union lacks same-status discrimination strategy |
| BP111 | Error | Declared results contract cannot be projected into aligned flattened outcomes |
| BP112 | Error | Host-neutral and host-specific no-result shapes are mixed |
| BP113 | Error | Exception translator ownership is ambiguous |
| BP114 | Error | Duplicate Results union member |
| BP115 | Error | Operation implements both ICommand and IQuery |

### Ergonomics (BP200–BP299)

| ID | Severity | Title |
|----|----------|-------|
| BP200 | Info | Handler method should be async |

## Authentication and authorisation (BPAU)

| ID | Severity | Title |
|----|----------|-------|
| BPAU001 | Warning | `[MustBeAuthenticated]` on non-HTTP operation (not a background task, where it turns the anonymous convention off) |
| BPAU002 | Error | Conflicting `[AllowAnonymous]` and `[MustBeAuthenticated]` |
| BPAU003 | Error | `[Resource]`/`[FromResource]` combined with `[AllowAnonymous]` |
| BPAU004 | Error | Empty scope pair value in `[Resource]` |
| BPAU005 | Error | Both `[FromResource]` and `[Resource]` applied |
| BPAU006 | Error | `[FromResource]` target has no `[Resource]` attributes |
| BPAU007 | Error | Resource key template `{Token}` does not match a property |
| BPAU008 | Warning | `[Resource]` on a background task that is not exposed over HTTP and not marked `[MustBeAuthenticated]` (never enforced). Narrowed from "any non-HTTP operation": other in-process operations are now checked by the providers (#78) |
| BPAU009 | Warning | Duplicate `[Resource]` scope pair |
| BPAU010 | Warning | Resource key template fallback is never used: not on the operation, nor on a linked resource type that lacks the primary |
| BPAU011 | Error | Resource key template token matches no readable property of a resource type the operation links from |

## Link generation (BPLG)

| ID | Severity | Title |
|----|----------|-------|
| BPLG001 | Error | Link format is invalid |
| BPLG003 | Error | Resource type is not `ILinkableResource` |
| BPLG004 | Warning | Ambiguous self-link declarations |
| BPLG005 | Error | Empty route template |
| BPLG006 | — | Retired in v1.0.3: a `[Link]` without a `Rel` is no longer an error anywhere; it is keyed by its operation's descriptor name, as legacy Blueprint keyed it |

## Validation (BPV)

| ID | Severity | Title |
|----|----------|-------|
| BPV001 | Error | `CompareAttribute` references a missing property |
| BPV002 | Error | `GreaterThanPropertyAttribute` references a missing property |
| BPV003 | Error | `RequiredIfAttribute` references a missing property |
| BPV004 | Error | `RequiredIfNotAttribute` references a missing property |
| BPV005 | Error | `NotAllowedIfAttribute` references a missing property |
| BPV006 | Error | `LessThanPropertyAttribute` references a missing property |
| BPV007 | Error | `MustBeGreaterThanOrEqualAttribute` references a missing property |
| BPV008 | Error | MinLength exceeds MaxLength |
| BPV009 | Error | Range minimum exceeds maximum |
| BPV010 | Error | StringLength minimum exceeds maximum |
| BPV011 | Error | RegularExpression has empty pattern |
| BPV012 | Info | `[Required]` is redundant on non-nullable value type |
| BPV013 | Info | IValidatableObject prevents compile-time validation |
| BPV014 | Warning | `[Required]` conflicts with `[DefaultValue]` |
| BPV015 | Error | `RequiredItemsIfAttribute` references a missing property |
| BPV016 | Error | `RequiredItemsIfNotAttribute` references a missing property |
| BPV017 | Error | Conditional `[Required*]` attribute silently passes empty collections |
| BPV018 | Error | `RequiredItemsIf*` attribute applied to a non-collection property |

### BPV017 — conditional `[RequiredIf]`/`[RequiredIfNot]` on a collection

`RequiredIf` and `RequiredIfNot` perform a scalar *presence* check: any non-`null` value satisfies them, and a collection's presence is never `null`-vs-empty aware. Applied to a list, an **empty** collection therefore passes even when the requirement is active. Use `[RequiredItemsIf]`/`[RequiredItemsIfNot]`, which inspect the item count, for a conditionally non-empty collection — or `[NotEmptyList]` when the requirement is unconditional.

```csharp
// ❌ BPV017 — empty Formats passes when RestrictFormats is true
[RequiredIf(nameof(RestrictFormats), true)]
public List<string>? Formats { get; set; }

// ✅ empty Formats is rejected when RestrictFormats is true
[RequiredItemsIf(nameof(RestrictFormats), true)]
public List<string>? Formats { get; set; }
```

Like the other conditional validators, `RequiredItemsIf`/`RequiredItemsIfNot` are cross-property constraints and are enforced at runtime only — they are intentionally absent from the generated OpenAPI schema.

### BPV018 — `[RequiredItemsIf]`/`[RequiredItemsIfNot]` on a non-collection

These attributes inspect an item count, so they only make sense on a collection property. On a scalar property, use `[RequiredIf]`/`[RequiredIfNot]` instead.

## Property ownership and binding (BPPO)

| ID | Severity | Title |
|----|----------|-------|
| BPPO001 | Error | Unsupported route property type |
| BPPO002 | Warning | Body-owned properties on GET operation |
| BPPO003 | Error | Unsupported query property type |
| BPPO004 | Error | `[FromBody]` on GET operation |
| BPPO005 | Error | `[FromRoute]` without matching route placeholder |
| BPPO006 | Error | Conflicting `[FromX]` binding attributes |
| BPPO007 | Error | Unsupported header/cookie property type |
| BPPO008 | Error | Malformed route placeholder |
| BPPO009 | Error | `[FromAuthContext]` ClaimType on non-string property |
| BPPO010 | Warning | `[FromAuthContext]` ClaimType on route-owned property |
| BPPO011 | Info | Property uses slow TypeConverter binding path |
| BPPO012 | Error | Route placeholder without matching property |
| BPPO013 | Info | Nullable route parameter |
| BPPO014 | Error | Stream body with another body property |
| BPPO015 | Error | Stream or file property on an operation that answers GET |
| BPPO016 | Error | Unsupported form field property type |
| BPPO017 | Error | Stream or multipart body property has no public getter or setter |

## OpenAPI (BPOA)

| ID | Severity | Title |
|----|----------|-------|
| BPOA001 | Error | Explicit HTTP method is null or whitespace |
| BPOA002 | Warning | Operation declares multiple conflicting HTTP methods |

## Operation discovery (BPOD)

| ID | Severity | Title |
|----|----------|-------|
| BPOD001 | Error | Host exposure not supported by enabled host contributor |
| BPOD002 | Error | Operation targets multiple enabled hosts |

## Resource events (BPRE)

| ID | Severity | Title |
|----|----------|-------|
| BPRE001 | Error | Resource event family must be partial |
| BPRE002 | Error | Resource event record must be partial |
| BPRE003 | Error | Ambiguous resource event constructor shape |
| BPRE004 | Error | Duplicate event reason within family |
| BPRE005 | Error | `ResourceObject` must be specified on attribute |
| BPRE006 | Error | Resource type missing `Id` property |

## Auditing (BPA)

| ID | Severity | Title |
|----|----------|-------|
| BPA001 | Warning | `[Sensitive]` has no effect on non-public properties |

## Persistence (BPP)

| ID | Severity | Title |
|----|----------|-------|
| BPP001 | — | Retired in v1.0.3: `[DoNotSaveChanges]` on a query now has effect, as the unit of work is saved after any operation without it |

## Messaging (BPM)

| ID | Severity | Title |
|----|----------|-------|
| BPM001 | Warning | `[DoNotPublishMessages]` on an operation that is neither a command nor a background task (no effect) |

## Background tasks (BPT)

| ID | Severity | Title |
|----|----------|-------|
| BPT001 | Warning | Background task handler returns a value that will be discarded |

## Typed HTTP clients (BPCL)

Reported for a `[BlueprintClient]` class and its `[BlueprintClientInclude]` attributes. Each include is reported at the attribute, each member at its name, and the class at its name. An include or member with a problem is left out of the generated half, which is still generated for the rest, so one mistake does not raise follow-on errors in the code that uses the client. A class that cannot be completed at all (BPCL001) gets no generated half.

| ID | Severity | Title |
|----|----------|-------|
| BPCL001 | Error | `[BlueprintClient]` class must be a partial, top-level, non-generic, non-static class (public or internal) |
| BPCL002 | Error | `[BlueprintClientInclude]` type must be a concrete, non-generic class implementing `IOperation` |
| BPCL003 | Error | Included operation declares no result (`IReturn<T>`) |
| BPCL004 | Error | Included operation has no typed request builder: no URL, named `Http` or declaring or inheriting a member named `Http`, nested in another type, not partial, or from an assembly not built with Blueprint's source generator |
| BPCL005 | Error | Operation included more than once, or two includes produce the same method name |
| BPCL006 | Error | Public method of the client class is generic, so cannot be repeated on the generated interface |
| BPCL007 | Warning | The `[JsonSerializable]` types of the client's `SerializerContext` do not cover a type an included operation needs: its request (a GET sends none), and what its result is read as, which for a resource event or `ResourceEventResult<T>` is the `T` it carries, for a `Results<T1, T2>` each member's (an `OkResult<T>`, `CreatedResult<T>` or `AcceptedResult<T>` member by its `T`), and for any other result, an `OkResult<T>`, `CreatedResult<T>` or `AcceptedResult<T>` called alone included, the result itself. The call is still generated, and reads the uncovered type through the options' reflection metadata |
| BPCL008 | Error | Included operation's result is one no response reader supports, such as a `StatusCodeResult` subclass with no public static `Instance`, an abstract resource event that does not declare `[JsonPolymorphic]` or `[JsonDerivedType]` itself (the event reader cannot make one; one that does is read as JSON), or a `Results<T1, T2>` with such a member |
| BPCL009 | Error | Member of the client class conflicts with a generated member: a member of any kind named `_client`, `_plan`, `_credential`, `_requestModifier`, `BlueprintClientPlan`, `WithCredential` or `WithRequestModifier`; a readonly field whose constructor parameter would be named `client`, `source`, `credential`, `requestModifier` or `modify`; a public method named as a generated operation method; or a constructor taking the generated constructors' parameter types, either `(BlueprintHttpClient, <each readonly field's type>)` or the clone constructor's `(<the class>, IUpstreamCredential, Action<HttpRequestMessage>)` |

## Gateway operations (BPGW)

BPGW001 to BPGW014, BPGW018 to BPGW021 and BPGW035 are reported for an operation marked `[Expose<TInner>]`; BPGW015 to BPGW017 for any operation, and BPGW017 also for an app, since hand-written and generated forwards read the same attributes. BPGW001, BPGW006, BPGW010 and BPGW018 are reported at the `[Expose<TInner>]` attribute; BPGW007, BPGW008, BPGW009, BPGW011, BPGW012, BPGW013, BPGW021 and BPGW035 at the binding attribute; BPGW014 at the hook; BPGW016 and BPGW017 at their attribute; BPGW015 at the second credential attribute; and the others at the operation's name. An operation with BPGW003, BPGW004, BPGW005, BPGW019 or BPGW020 gets no pipeline. An invalid binding is ignored by the generated forward, so its property stays in the public shape until it is fixed.

| ID | Severity | Title |
|----|----------|-------|
| BPGW001 | Error | `[Expose<TInner>]` in a project that sets the MSBuild property `BlueprintGatewayAllowExpose` to `false`, as a public API gateway does, so that each public contract is written out |
| BPGW002 | Error | Gateway operation declares none of `[AllowAnonymous]`, `[MustBeAuthenticated]`, `[Resource]` or `[FromResource]` from `Blueprint.Model.Attributes`. The inner operation's authorisation is never copied, and no other attribute counts |
| BPGW003 | Error | No `[BlueprintClient]` visible to the gateway includes the inner operation: none declared in the project with `[BlueprintClientInclude(typeof(TInner))]`, and no generated client in a referenced assembly with a method for it |
| BPGW004 | Error | More than one `[BlueprintClient]` includes the inner operation, so the upstream to forward to is ambiguous |
| BPGW005 | Error | Gateway operation has a hand-written handler, inline or external, but `[Expose<TInner>]` generates its handler |
| BPGW006 | Error | `[Expose<TInner>]` class declares no operation interface, or one with no result. Declare the inner operation's own, for example `partial class X : IQuery<R>`, since no generator can see an interface another generator emits |
| BPGW007 | Error | `[Bind]` or `[Hide]` names a property the inner operation has no public settable property of |
| BPGW008 | Error | `[Bind]` sets none, or more than one, of `FromClaim`, `FromRoute`, `FromProperty` and `Value`; or `[assembly: GatewayBind]` sets no `FromClaim` |
| BPGW009 | Error | `[Hide]` on a property the inner operation requires (`required`, or `[Required]`), on an operation with no `OnMap` or `OnMapAsync` to fill it. Bind it, or assign it in a hook |
| BPGW010 | Error | The inner operation's first route, the URL the forward sends, has a placeholder the gateway's route lacks, and the property is neither copied, bound nor hidden, so the inner request builder would have no value for it. A copied property, contributed or declared with the inner property's type, satisfies the placeholder wherever the gateway reads it from, such as the query string |
| BPGW011 | Error | A binding's source does not convert to the inner property's type: a claim bound to a type route binding cannot read, or a `FromRoute`, `FromProperty` or `Value` of another type |
| BPGW012 | Error | `[Bind(FromRoute)]` names a placeholder any of the gateway's own routes lacks or that binds no property it declares, or `[Bind(FromProperty)]` names a property it does not declare; or the property either names has no public getter, so the forward cannot read it; or a route placeholder's property is one the route does not fill (no public setter, or `[FromQuery]`, `[FromBody]`, `[FromHeader]`, `[FromCookie]` or `[FromAuthContext]`) |
| BPGW013 | Error | The same inner property is bound or hidden more than once on one operation |
| BPGW014 | Error | `OnMap` or `OnMapAsync` cannot be called by the generated forward: it has to be a single `internal` or `public` method, instance or `static` (a static one is called on the operation type), whose first parameter is `TInner`, not `[Conditional]`, `OnMap` returning `void` and not `async`, and `OnMapAsync` returning `ValueTask` or `Task` |
| BPGW015 | Error | Gateway operation carries more than one upstream credential attribute (`[UpstreamCredential<T>]` or one deriving from it); its forwards authenticate with one strategy |
| BPGW016 | Error | An upstream credential attribute selects a type that does not implement `Blueprint.HttpClient.IUpstreamCredentialStrategy` |
| BPGW017 | Error | `[TranslateError(fromType, to)]` on an operation or app names no static `ApiExceptionFactory` field or property of that class called `to`. Members inherited from a base class do not count, as the forward does not read them |
| BPGW018 | Error | A contributed property's attribute names a type the gateway's assembly cannot access, the attribute's own or one in its arguments (`typeof`, an enum value, an array), or is applied through a constructor or named argument it cannot access. It is not public, nor internal to an assembly with `InternalsVisibleTo` the gateway, so the emitted property cannot carry it, and the gateway would bind, serialize or validate the property otherwise than the inner operation |
| BPGW019 | Error | The inner operation has a `required` member the generated forward cannot set: the gateway declares that property with another type, or it is a required field, and no binding sets it. The operation gets no pipeline |
| BPGW020 | Error | A property the generated forward copies to the inner operation has no public getter: one the gateway declares, which the pipeline cannot read, or the inner one, whose default the contributed property cannot start from, so the forward would replace it whenever the caller omits the value. The operation gets no pipeline |
| BPGW021 | Error | `[GatewayBind]`, `[Bind]` or `[Hide]` fills or hides a property the gateway operation declares itself, from anything but that declaration. The declaration would stay in the public shape, bound from the request and validated, while the forward ignored it. Remove it, or forward it with `[Bind("X", FromProperty = "X")]` |
| BPGW029 | Error | The inner operation's first route ends in optional segments (`/reports/{Year?}/{Month?}`), and the forward leaves one of them null (it is not in the gateway's route, copied or bound, or it is bound to `null`) but can fill a later one. Leaving the first out would move the later value into its place, so the inner request builder builds no URL whenever the later one has a value. Fill the earlier one, hide it when the inner operation's initialiser or the gateway's `OnMap` sets it (as for BPGW010), or hide the later one. An optional placeholder after a `.` shares its segment, so it does not count |
| BPGW035 | Error | `[Hide]` on a property the inner operation requires, which the operation's `OnMap` or `OnMapAsync` never assigns. The hook has to assign `inner.X` unconditionally in its own body, by any assignment operator, so the check can see it: as its expression body or a statement of its body (a nested plain block counts). An assignment under an `if`, a loop, a `try`, a lambda or a local function, after a `return` or `goto` of the hook's own, or in a helper it calls, does not count, and none does in a hook that rebinds its `inner` parameter, by assignment or as a `ref` or `out` argument. Assign it in the hook, or bind it instead |

## Module system (BPMOD)

| ID | Severity | Title |
|----|----------|-------|
| BPMOD001 | Error | Duplicate unique contributor |
| BPMOD002 | Error | Missing contributor dependency |
| BPMOD003 | Error | Cyclic contributor dependency |
| BPMOD004 | Error | Missing required variable |
| BPMOD005 | Error | Missing operation type |
| BPMOD006 | Error | Duplicate debug-manifest entry |

## Migration diagnostics (BPMIG)

See also [consumer-migration-guide.md](consumer-migration-guide.md).

| ID | Severity | Category | Title |
|----|----------|----------|-------|
| BPMIG001 | Info | Auto-fixable | Remove redundant `AddOpenApi()` activation |
| BPMIG002 | Info | Auto-fixable | Remove redundant `AddHateoasLinks()` activation (links are on by default; an app that never called it needs `DisableHateoasLinks()`, which no diagnostic can prompt) |
| BPMIG003 | Info | Auto-fixable | Remove redundant `AddAuthorisation()` activation |
| BPMIG100 | Warning | Config required | Migrate configured `AddOpenApi(...)` usage |
| BPMIG101 | Warning | Config required | Migrate configured `AddAuthentication(...)` usage |
| BPMIG200 | Warning | Consumer rewrite | Migrate `[FromResource]` usage |
| BPMIG201 | Warning | Consumer rewrite | Migrate `{Auth:...}` placeholder usage |
| BPMIG203 | Warning | Consumer rewrite | Migrate `[Resource(noun, verb, path)]` resource key template (drop if pure auth-claim, else rewrite to an explicit binding) |
| BPMIG300 | Warning | Namespace rename | Update legacy Blueprint namespace using directive |
| BPMIG301 | Warning | Consumer rewrite | `IMiddlewareBuilder` replaced by source-generator modules |
| BPMIG302 | Warning | Consumer rewrite | `BlueprintApiBuilder` replaced by `AddBlueprintApi<TApp, TExec>` |
| BPMIG303 | Warning | Auto-fixable | Remove obsolete Blueprint namespace |
| BPMIG400 | Warning | Auto-fixable | Add `partial` to operation class |
| BPMIG401 | Warning | Auto-fixable | Add `partial` to API resource class |
| BPMIG402 | Warning | Auto-fixable | Add missing using for relocated Blueprint type |

### Informational / disabled

| ID | Severity | Category | Title |
|----|----------|----------|-------|
| BPMIG202 | Info | Informational | Abstract generic base implements IQuery/ICommand (now supported) |

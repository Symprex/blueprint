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

#### Phasing BP012/BP013 in

Both are errors by default. To report them as warnings while a codebase is fixed, set Blueprint's own key in the consuming project's `.editorconfig` (a `[*.cs]` section of the file that declares the app, or any section that covers it):

```ini
[*.cs]
blueprint_diagnostic.BP012.severity = warning   # error (default), warning, suggestion, silent or none
blueprint_diagnostic.BP013.severity = warning
```

`dotnet_diagnostic.BP012.severity` in an `.editorconfig` does **not** work: the compiler applies only global configuration to a source generator's diagnostics and keeps `dotnet_diagnostic` keys to itself. In a `.globalconfig` (`is_global = true`) it does work, as a compiler-applied alternative. With `TreatWarningsAsErrors`, a warning still fails the build unless the project also sets `<WarningsNotAsErrors>$(WarningsNotAsErrors);BP012;BP013</WarningsNotAsErrors>`.

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
| BPAU001 | Warning | `[MustBeAuthenticated]` on non-HTTP operation |
| BPAU002 | Error | Conflicting `[AllowAnonymous]` and `[MustBeAuthenticated]` |
| BPAU003 | Error | `[Resource]`/`[FromResource]` combined with `[AllowAnonymous]` |
| BPAU004 | Error | Empty scope pair value in `[Resource]` |
| BPAU005 | Error | Both `[FromResource]` and `[Resource]` applied |
| BPAU006 | Error | `[FromResource]` target has no `[Resource]` attributes |
| BPAU007 | Error | Resource key template `{Token}` does not match a property |
| BPAU008 | Warning | `[Resource]` on non-HTTP operation |
| BPAU009 | Warning | Duplicate `[Resource]` scope pair |

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

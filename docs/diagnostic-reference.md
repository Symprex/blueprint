# Diagnostic reference

Quick reference for all compile-time diagnostics emitted by the Blueprint source generator.
For detailed explanations, see [compile-time-validation.md](compile-time-validation.md).

## Core diagnostics (BP)

### Shape and registration (BP001–BP099)

| ID | Severity | Title |
|----|----------|-------|
| BP001 | Error | Class must be partial (API resource) |
| BP002 | Error | Class must be partial (app model) |
| BP004 | Error | Cannot parse `[BlueprintAppScan]` attribute |
| BP005 | Error | Class must be partial (descriptor) |
| BP006 | Error | Serializer context is missing required response body coverage |
| BP007 | Error | App is missing a serializer context declaration |
| BP008 | Error | Serializer-covered body type uses unsupported OpenAPI validation shape |
| BP009 | Error | Duplicate descriptor name after Query/Command suffix stripping |
| BP010 | Warning | Non-evaluable ApiExceptionFactory initializer |

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
| BPAU007 | Warning | Resource key template `{Token}` does not match a property |
| BPAU008 | Warning | `[Resource]` on non-HTTP operation |
| BPAU009 | Warning | Duplicate `[Resource]` scope pair |

## Link generation (BPLG)

| ID | Severity | Title |
|----|----------|-------|
| BPLG001 | Error | Link format is invalid |
| BPLG003 | Error | Resource type is not `ILinkableResource` |
| BPLG004 | Warning | Ambiguous self-link declarations |
| BPLG005 | Error | Empty route template |
| BPLG006 | Error | Link missing Rel value |

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
| BPV012 | Warning | `[Required]` is redundant on non-nullable value type |
| BPV013 | Info | IValidatableObject prevents compile-time validation |
| BPV014 | Warning | `[Required]` conflicts with `[DefaultValue]` |

## Property ownership and binding (BPPO)

| ID | Severity | Title |
|----|----------|-------|
| BPPO001 | Error | Unsupported route property type |
| BPPO002 | Warning | Body-owned properties on GET/DELETE operation |
| BPPO003 | Error | Unsupported query property type |
| BPPO004 | Error | `[FromBody]` on GET/DELETE operation |
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
| BPP001 | Warning | `[DoNotSaveChanges]` on non-command operation (no effect) |

## Messaging (BPM)

| ID | Severity | Title |
|----|----------|-------|
| BPM001 | Warning | `[DoNotPublishMessages]` on non-command operation (no effect) |

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
| BPMIG002 | Info | Auto-fixable | Remove redundant `AddHateoasLinks()` activation |
| BPMIG003 | Info | Auto-fixable | Remove redundant `AddAuthorisation()` activation |
| BPMIG100 | Warning | Config required | Migrate configured `AddOpenApi(...)` usage |
| BPMIG101 | Warning | Config required | Migrate configured `AddAuthentication(...)` usage |
| BPMIG200 | Warning | Consumer rewrite | Migrate `[FromResource]` usage |
| BPMIG201 | Warning | Consumer rewrite | Migrate `{Auth:...}` placeholder usage |
| BPMIG300 | Warning | Namespace rename | Update legacy Blueprint namespace using directive |
| BPMIG301 | Warning | Consumer rewrite | `IMiddlewareBuilder` replaced by source-generator modules |
| BPMIG302 | Warning | Consumer rewrite | `BlueprintApiBuilder` replaced by `AddBlueprintApi<TApp, TExec>` |

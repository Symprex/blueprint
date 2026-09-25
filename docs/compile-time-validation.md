# Compile-time validation

This repository prefers deterministic build-time feedback over runtime surprises.

If the compiler or generator can detect a bad configuration, it should do so with a precise diagnostic instead of letting the runtime discover the problem later.

## Current validation surfaces

### Resource, app, and descriptor shape

- `BP001` comes from `ApiResourceSourceGenerator` and enforces partial resource types when the generator needs to synthesize the `Object` property (`src\Blueprint.SourceGenerator\Generators\ApiResources\ApiResourceSourceGenerator.cs`).
- `BP002`, `BP003`, and `BP004` come from `AppDataModelSourceGenerator` and cover app registration shape and scan issues (`src\Blueprint.SourceGenerator\Generators\App\AppDataModelSourceGenerator.cs`).
- `BP006` now also comes from `AppDataModelSourceGenerator` and fails HTTP-visible app registrations when the declared `[BlueprintAppSerializerContext(JsonSerializerContextType = typeof(...))]` omits one or more descriptor-derived response body payload types. The generator reuses the shared descriptor-planning path before app registration emits source, and it now persists the declared `JsonSerializable(...)` body-type inventory on the generated app model, so later OpenAPI/schema slices can trust the app-owned serializer contract without rediscovering HTTP body shapes in a second code path.
- `BP007` now also comes from `AppDataModelSourceGenerator` and fails HTTP-visible app registrations when descriptor-derived serializer coverage requirements exist but the app root declares no `[BlueprintAppSerializerContext(JsonSerializerContextType = typeof(...))]` at all. That missing-configuration diagnostic stays on the app-owned declaration seam instead of routing through runtime host options or executor inspection.
- `BP005` comes from `OperationDescriptorSourceGenerator` and enforces required partial types for descriptor generation (`src\Blueprint.SourceGenerator\Generators\Descriptors\OperationDescriptorSourceGenerator.cs`).
- `BP011` comes from `AppDataModelSourceGenerator` and fails an app whose `[BlueprintAppOperationNames(Remove = ...)]` gives two of its operations the same descriptor name. The check runs on the names the app will actually register — named as legacy named them (`Query`/`Command` removed anywhere, then legacy's acronym-folding PascalCasing, `OperationNameHelpers.GetDescriptorName`), then with every occurrence of the removed text taken out of that cased name — and reports only the collisions the removal introduced, leaving those the naming already caused to `BP009`. It also fails an operation whose name the removal empties entirely (`PortalQuery` → `Portal` → `""`), which would otherwise have an empty `operationId`. It is reported on the attribute, because changing the removal is one of the two repairs.

### Pipeline and handler validation

`src\Blueprint.SourceGenerator\Generators\Pipelines\DiagnosticDescriptors.cs` defines the main pipeline diagnostics:

| Range | Meaning |
| --- | --- |
| `BP100`-`BP199` | hard errors around missing handlers, duplicate handlers, invalid signatures, and unsupported shapes |
| `BP200`-`BP299` | warnings or info around performance and ergonomics, such as async usage or static handlers |
| `BP300`-`BP399` | informational diagnostics that can help inspect generator behavior |

Current pipeline IDs in the `BP100`-`BP104` handler-validation range:

- `BP100`: no handler found for operation.
- `BP101`: multiple handlers found for operation (each operation can have only one handler).
- `BP104`: operation class must be partial to support generated pipelines.

> IDs `BP102` and `BP103` are reserved for future handler-signature and parameter-resolution diagnostics.

Current pipeline IDs in the `BP200` performance/ergonomics range:

- `BP200`: handler method should be async (info — not a build error, guidance only).

These diagnostics should stay stable and descriptive because they are part of the repo's human and AI feedback loop.

Recent pipeline-contract coverage in that range includes `BP105` for nullable plain-value handler returns, `BP106` for unsupported `Results<T1, T2>` member types, `BP107` for handler returns that do not match the declared operation result contract, `BP108` for host-exposed semantic result contracts that the current enabled host executor set cannot translate, `BP109` for host-exposed semantic result unions whose supported members still collapse to the same current host response shape, `BP110` for host-exposed semantic result unions that still share the same HTTP status code without an explicit host/client discrimination strategy, `BP111` for `Results<T1, T2>` contracts whose flattened descriptor outcomes would drift away from the declared semantic member set, `BP112` for HTTP-visible unions that try to mix host-neutral `NoResultOperationResult` with host-owned no-content/status-only result families such as `HttpNoContentResult`, and `BP113` for ordered pipeline exception-translator policies whose non-fallback translators claim overlapping exception families. The current HTTP executor surface now also includes the host-neutral `CreatedResult<T>` semantic shape, which maps to body-bearing `201 Created` responses, the host-neutral `AcceptedResult<T>` semantic shape, which maps to body-bearing `202 Accepted` responses, the host-neutral `NotFoundResult<T>` semantic shape, which maps to body-bearing `404 Not Found` responses, the host-neutral `ConflictResult<T>` semantic shape, which maps to body-bearing `409 Conflict` responses, the canonical `ValidationFailedResult` semantic shape, which maps to body-bearing `422 Unprocessable Entity` problem responses, and the host-neutral `KeyOnlyResourceEventResult` semantic shape, which maps to empty `201 Created` responses by convention. `ValidationFailedResult` is now both the canonical validation-failure name and part of the supported HTTP executor surface, so host-exposed operations that declare it no longer trigger `BP108` simply for using the canonical validation-failure contract. `Blueprint.Runtime.Exec.UnhandledExceptionResult` is now the canonical internal unhandled-exception fallback name, with `Blueprint.UnhandledExceptionOperationResult` retained as a compatibility alias, and both names should continue to fail with `BP111` when a declared public result contract tries to surface them as semantic outcomes. `BP113` keeps the exception shell deterministic too: only the single catch-all fallback translator may overlap a more specific translator family, the validated ordering policy still emits that fallback last, and package-provided translators now enter that same overlap check through `IExceptionTranslationContributor` instead of bypassing it through ad-hoc generator wiring. Keep those rules compile-time-first: the generator should reject unsupported or ambiguous result shapes before runtime execution needs to guess how to normalize them, and the compile-time response-shape rules should preserve the deliberate distinction between `CreatedResult<T>` (`201` with body), `AcceptedResult<T>` (`202` with body), `NotFoundResult<T>` (`404` with body), `ConflictResult<T>` (`409` with body), `ValidationFailedResult` (`422` with body), `KeyOnlyResourceEventResult` (`201` empty), `NoResultOperationResult` (`200` empty), and `HttpNoContentResult` (`204` empty). The HTTP executor surface now also includes `PlainTextResult` (`200` with `text/plain` body), `RedirectResult` (`302` or configurable status with `Location` header, no body), and `FileStreamResult` (`200` with streamed binary body and optional `Content-Disposition` header), so `BP108` no longer fires for operations that declare these convenience result types in their HTTP-exposed contracts.

### Migration and refactoring diagnostics

Large-scale migrations should use a dedicated diagnostic range rather than mixing mechanically-fixable migration findings into the ordinary generator IDs.

Current reservation:

| Range | Meaning |
| --- | --- |
| `BPMIG001`-`BPMIG099` | auto-fixable migration diagnostics |
| `BPMIG100`-`BPMIG199` | configuration-required migration findings |
| `BPMIG200`-`BPMIG299` | consumer-extension rewrites or custom migration work |
| `BPMIG300`-`BPMIG399` | manual design-review items that should not be silently auto-fixed |
| `BPMIG400`-`BPMIG499` | auto-fixable code-structure diagnostics (partial keyword, required modifier) |

The migration analyzer seam now lives in `src\Blueprint.Migration.Analyzers\MigrationDiagnosticDescriptors.cs`, with analyzer proof in `src\Blueprint.Migration.Analyzers.Tests` and code-fix implementation in `src\Blueprint.Migration.Analyzers.CodeFixes`. The live auto-fixable activation diagnostics now include `BPMIG001` for redundant parameterless `BlueprintApiBuilder.AddOpenApi()` calls, `BPMIG002` for redundant parameterless `AddHateoasLinks()` calls, and `BPMIG003` for redundant parameterless `AddAuthorisation()` calls, all of which describe capabilities that the source-generated model should infer without an activation switch. The configuration-required companion rules now start with `BPMIG100` for configured `AddOpenApi(Action<OpenApiOptions>)` usage and `BPMIG101` for configured `AddAuthentication(...)` usage, both of which preserve consumer-owned setup work instead of treating it as a safe auto-fix. The consumer-extension rewrite rules now also include `BPMIG200` for legacy `[FromResource(...)]` usage and `BPMIG201` for raw `{Auth:...}` placeholder usage, so both auth/resource inheritance and auth-template expansion work enter the shared analyzer/report/codemod flow before descriptor lowering automation exists. The code-structure auto-fix range starts with `BPMIG400` for concrete operation classes (implementing `ICommand<T>` or `IQuery<T>`) that are missing the `partial` keyword — the highest-volume migration automation for Signature 365 (~240+ classes). The structured report contract for that family now lives in `src\Blueprint.Migration.Analyzers\MigrationReport.cs` and `src\Blueprint.Migration.Analyzers\DiagnosticFinding.cs`: findings are ordered deterministically by diagnostic ID, classified into the same planning buckets (`auto-fixable`, `configuration required`, `consumer-extension rewrite required`, `manual design review required`), point at the durable migration-plan path for the owning workstream, and are now guarded by explicit JSON contract tests that validate the required top-level and finding-level payload shape for `migration-diagnostics`. The repo-native codemod seam now spans `src\Blueprint.Codemod\SolutionAnalysisRunner.cs` for dry-run SARIF collection, `src\Blueprint.Codemod\SarifDiagnosticReader.cs` for explicit SARIF input validation, `src\Blueprint.Codemod\TransformationApplier.cs` plus `ApplyModeOptions.cs` for the first apply-mode foundation, and `src\Blueprint.Codemod\CodemodArtifactWriter.cs` plus `CodemodMarkdownSummaryWriter.cs` for deterministic `codemod-report.json` / `codemod-report.md` emission through `--output <dir>`. Supported migration fixes (currently `BPMIG001`-`BPMIG003` and `BPMIG400`) are replayed through the existing analyzer code-fix providers inside an `MSBuildWorkspace`, repeat runs stay idempotent, malformed dry-run SARIF now fails loudly when required `runs`, `results`, or string `ruleId` fields are missing, non-auto-fixable findings such as `BPMIG100`, `BPMIG101`, `BPMIG200`, and `BPMIG201` remain in the post-apply report rather than being silently rewritten, and all artifact projections stay sourced from the canonical `MigrationReport` payload. Future migration diagnostics should extend that shared analyzer/test/report/codemod seam rather than introducing ad hoc project layouts.

Migration diagnostics should be paired with machine-readable reporting such as SARIF and the repo-native JSON report object model so that larger refactors remain reviewable and automatable without log scraping.

### Link and route validation

`LinkGenerationModule` defines `BPLG001` through `BPLG003` (`src\Blueprint.SourceGenerator\Modules\LinkGeneration\LinkGenerationModule.cs`):

- placeholder-to-property validation for route templates
- duplicate URL detection
- resource-type compatibility checks for linkable resources

This is the clearest current example of the repository principle that string literals should be validated against symbols when possible.

`BPLG004` additionally warns when multiple self-link URL templates target the same resource type, creating ambiguity in link population.

`BPLG007` is a cross-operation route collision diagnostic raised in `AppDataModelSourceGenerator.FindOperations`. It fires when two distinct operations declare the same route template and HTTP method combination across their link attributes. Both endpoints would be registered by ASP.NET Core, causing an `AmbiguousMatchException` at request time. The runtime already logs this situation, but this diagnostic catches it at build time. It is a **Warning** rather than an Error because temporary collisions may exist during migration.

### Property ownership and binding diagnostics

Property ownership and binding diagnostics detect misapplied binding attributes, unsupported conversion types, and body semantics that conflict with the HTTP method.

Reserved direction:

| Range | Meaning |
| --- | --- |
| `BPPO001`-`BPPO099` | property ownership and binding diagnostics for invalid declarative contracts that the generator can prove at build time |

Current property ownership IDs in active use:

- `BPPO001`: route-bound property has a type that cannot be converted from a string route value.
- `BPPO002`: operation uses GET/DELETE but has properties that would be body-owned by convention (warning).
- `BPPO003`: query-bound property has a type that cannot be converted from a string query value.
- `BPPO004`: explicit `[FromBody]` on a GET/DELETE operation (error — these methods should not have request bodies).
- `BPPO005`: explicit `[FromRoute]` without a matching route placeholder in the URL template.
- `BPPO006`: multiple conflicting `[FromX]` binding attributes on the same property.
- `BPPO007`: header- or cookie-bound property has a type that cannot be converted from a string value.
- `BPPO008`: malformed route placeholder (e.g. missing closing brace or invalid characters).
- `BPPO009`: `[FromAuthContext(ClaimType = "...")]` on a non-string property — only string properties support compile-time claim mapping (error).
- `BPPO010`: `[FromAuthContext(ClaimType = "...")]` on a property that is also route-bound — route binding takes precedence, making the ClaimType dead configuration (warning).
- `BPPO011`: route/query property type uses `[TypeConverter]` instead of the faster `IParsable<T>` / static `TryParse` path. TypeConverter uses exception-based control flow for invalid input (info).
- `BPPO012`: route template contains a placeholder with no matching operation property, so the generated pipeline would never be able to populate that route segment (error).
- `BPPO013`: route-bound property is nullable even though route parameters are always required, highlighting a migration contract mismatch before runtime (warning).

### Operation discovery and host diagnostics

Operation-discovery and host-exposure checks should use their own stable range instead of borrowing link or validation IDs.

Reserved direction:

| Range | Meaning |
| --- | --- |
| `BPOD001`-`BPOD099` | operation-discovery and host-exposure diagnostics for descriptor/app-registration contracts that the generator can prove at build time |

Current operation-discovery IDs in active use:

- `BPOD001`: descriptor planning recorded host exposure that no enabled host contributor supports.
- `BPOD002`: descriptor planning recorded exposure to more than one enabled host contributor for the same operation.

### OpenAPI descriptor diagnostics

OpenAPI descriptor metadata checks should use their own stable range instead of borrowing the link, validation, or host-exposure IDs.

Reserved direction:

| Range | Meaning |
| --- | --- |
| `BPOA001`-`BPOA099` | OpenAPI descriptor diagnostics for invalid OpenAPI-facing declarative contracts that the generator can prove at build time |

Current OpenAPI descriptor IDs in active use:

- `BPOA001`: `HttpMethodAttribute` declares a null, empty, or whitespace-only explicit HTTP method for an HTTP-exposed operation.
- `BPOA002`: operation declares multiple distinct HTTP method attributes (e.g. both `[HttpGet]` and `[HttpPost]`) — warning, since each operation should map to exactly one HTTP method.

### Validation contributor diagnostics

Validation-oriented compile-time checks should use a dedicated range instead of borrowing the link or foundation IDs.

Reserved direction:

| Range | Meaning |
| --- | --- |
| `BPV001`-`BPV099` | validation contributor diagnostics for invalid declarative contracts that the generator can prove at build time |

Typical examples include validation attributes that name other members with strings, such as `CompareAttribute.OtherProperty` pointing at a property that does not exist on the operation type. When those contracts are knowable during generation, prefer a build-time diagnostic over waiting for runtime validation to fail indirectly.

Current validation contributor IDs in active use:

- `BPV001`: `CompareAttribute` references a property that does not exist on the operation type.
- `BPV002`: `GreaterThanPropertyAttribute` references a property that does not exist on the operation type.
- `BPV003`: `RequiredIfAttribute` references a property that does not exist on the operation type.
- `BPV004`: `RequiredIfNotAttribute` references a property that does not exist on the operation type.
- `BPV005`: `NotAllowedIfAttribute` references a property that does not exist on the operation type.
- `BPV006`: `LessThanPropertyAttribute` references a property that does not exist on the operation type.
- `BPV007`: `MustBeGreaterThanOrEqualAttribute` references a property that does not exist on the operation type.
- `BPV015`: `RequiredItemsIfAttribute` references a property that does not exist on the operation type.
- `BPV016`: `RequiredItemsIfNotAttribute` references a property that does not exist on the operation type.
- `BPV017`: a scalar conditional-required attribute (`RequiredIf`/`RequiredIfNot`) is applied to a collection property, where it silently passes empty collections. Use `RequiredItemsIf`/`RequiredItemsIfNot` (or `NotEmptyList` for an unconditional requirement).
- `BPV018`: a collection-aware conditional attribute (`RequiredItemsIf`/`RequiredItemsIfNot`) is applied to a non-collection property. Use `RequiredIf`/`RequiredIfNot` for scalars.

(`BPV008`–`BPV014` cover conflicting/redundant constraint shapes; see the [diagnostic reference](diagnostic-reference.md).)

### Resource event family diagnostics

Resource-event family declarations (the `[ResourceEventFamily]` authoring surface) have their own stable range for compile-time shape and contract validation.

Reserved direction:

| Range | Meaning |
| --- | --- |
| `BPRE001`-`BPRE099` | resource event family diagnostics for invalid event declarations that the generator can prove at build time |

Current resource event family IDs in active use:

- `BPRE001`: the resource event family class is not declared as `partial`.
- `BPRE002`: a nested event record within the family is not declared as `partial`.
- `BPRE003`: ambiguous constructor shape — cannot infer body vs key without explicit `[ResourceEventKey]`/`[ResourceEventBody]` attributes.
- `BPRE004`: duplicate reason string within the same family.
- `BPRE005`: `ResourceObject` property is not set on the `[ResourceEventFamily]` attribute.
- `BPRE006`: the family resource type does not have a public `Id` property required for body-bearing events.

### Persistence diagnostics

Persistence and unit-of-work coordination diagnostics detect misapplied attributes and configuration that the generator can prove ineffective at build time.

Reserved direction:

| Range | Meaning |
| --- | --- |
| `BPP001`-`BPP099` | persistence diagnostics for invalid or no-effect declarative contracts that the generator can prove at build time |

Current persistence IDs in active use:

- `BPP001`: `[DoNotSaveChanges]` applied to a non-command operation where it has no effect (only `ICommand` operations trigger the `UnitOfWorkModule`).

### Messaging diagnostics

Messaging diagnostics detect misapplied messaging attributes that have no observable effect at runtime.

Reserved direction:

| Range | Meaning |
| --- | --- |
| `BPM001`-`BPM099` | messaging diagnostics for invalid or no-effect declarative contracts that the generator can prove at build time |

Current messaging IDs in active use:

- `BPM001`: `[DoNotPublishMessages]` applied to an operation that is neither a command nor a background task, where it has no effect (warning — only `ICommand` and `IBackgroundTask` operations trigger the `MessageOutboxModule`).

### Auditing diagnostics

Auditing diagnostics detect misapplied audit-related attributes that have no observable effect at runtime.

Reserved direction:

| Range | Meaning |
| --- | --- |
| `BPA001`-`BPA099` | auditing diagnostics for invalid or no-effect declarative contracts that the generator can prove at build time |

Current auditing IDs in active use:

- `BPA001`: `[Sensitive]` applied to a non-public property where it has no effect (warning — only public properties appear in the audit metadata surface).

### Background task diagnostics

Background task diagnostics detect handler signatures that produce values which will be silently discarded during background execution.

Reserved direction:

| Range | Meaning |
| --- | --- |
| `BPT001`-`BPT099` | background task diagnostics for configurations that the generator can prove problematic at build time |

Current background task IDs in active use:

- `BPT001`: background task handler returns a value that will be discarded (warning — background tasks run without a consumer for the returned value; prefer `void`/`Task` or `OperationResult`).

### Authentication and authorisation diagnostics

Authentication and access-mode diagnostics detect conflicting or misapplied auth attributes and project explicit access-mode metadata into descriptors.

Reserved direction:

| Range | Meaning |
| --- | --- |
| `BPAU001`-`BPAU099` | authentication and authorisation diagnostics for invalid or conflicting declarative contracts that the generator can prove at build time |

Current authentication IDs in active use:

- `BPAU001`: `[MustBeAuthenticated]` applied to an operation that is not exposed via an HTTP host (warning — the authentication pipeline check cannot be generated without HTTP context).
- `BPAU002`: Both `[AllowAnonymous]` and `[MustBeAuthenticated]` applied to the same operation (error — the access mode must be unambiguous).
- `BPAU003`: `[Resource]` or `[FromResource]` combined with `[AllowAnonymous]` on the same operation (error — permissioned operations cannot simultaneously allow anonymous access).
- `BPAU004`: `[Resource]` attribute with an empty or whitespace-only noun or verb value (error — scope pairs must be meaningful).
- `BPAU005`: Both `[FromResource]` and `[Resource]` applied to the same operation (error — use one mechanism to declare scope pairs, not both).
- `BPAU006`: `[FromResource(typeof(X))]` referencing a type that has no `[Resource]` attributes (error — the target must declare at least one scope pair to inherit).
- `BPAU007`: Resource key template token `{PropertyName}` references a property that does not exist on the operation type AND there is no valid fallback path (error — authorization enforcement is suppressed, which is a security issue).
- `BPAU008`: `[Resource]` or `[FromResource]` on an operation that is not exposed via an HTTP host (warning — the authorization pipeline check cannot be generated without HTTP context to extract the principal; descriptor metadata is still projected).
- `BPAU009`: Duplicate noun+verb scope pair on the same operation via multiple `[Resource]` attributes (warning — redundant metadata, usually a copy-paste error).
- `BPAU010`: Fallback property path in `{Primary:Fallback}` syntax does not exist on the operation type, but the primary property does (warning — authorization code is still generated using the primary; the fallback is dead code that should be removed).

### Validation fast-path strategies

The `ValidationModule` plans each property independently through extracted fast-path strategies, falling back to `ValidationRuntime.ValidateProperty(...)` per property when a strategy cannot handle an attribute at compile time. Type-level validation (`IValidatableObject`) uses the whole-object `Validator.TryValidateObject(...)` fallback.

Current fast-path strategy coverage (in planning order):

| Strategy | Attribute | Condition | Fallback trigger |
| --- | --- | --- | --- |
| Required | `RequiredAttribute` | exact default constructor only | custom error message, display-name shaping |
| MaxLength | `MaxLengthAttribute` | string properties, default constructor | non-string types, custom error message |
| MinLength | `MinLengthAttribute` | string properties, default constructor | non-string types, custom error message |
| StringLength | `StringLengthAttribute` | string properties, default constructor | non-string types, custom error message |
| EmailAddress | `EmailAddressAttribute` | default constructor | custom error message |
| Phone | `PhoneAttribute` | default constructor, delegates to `new PhoneAttribute().IsValid()` | custom error message |
| Url | `UrlAttribute` | default constructor, delegates to `new UrlAttribute().IsValid()` | custom error message |
| Range | `RangeAttribute` | `Range(int, int)` and `Range(double, double)` constructors, nullable value types | Type overloads, named arguments |
| RegularExpression | `RegularExpressionAttribute` | single-arg constructor with string pattern | named arguments (e.g. `ErrorMessage`), multiple constructor args |
| Compare | `CompareAttribute` | same-type properties, compile-time `BPV001` for missing targets | n/a |
| GreaterThanProperty | `GreaterThanPropertyAttribute` | same-type numeric/decimal/DateTime including nullable pairs, compile-time `BPV002` | mixed nullable/non-nullable, unsupported types |
| GreaterThan | `GreaterThanAttribute` | numeric/decimal including nullable, default constructor | customized instances |
| GreaterThanOrEqualTo | `GreaterThanOrEqualToAttribute` | numeric/decimal including nullable, `int` threshold constructor with optional custom message | unsupported types, named arguments |
| LessThanProperty | `LessThanPropertyAttribute` | same-type numeric/decimal/DateTime including nullable pairs, compile-time `BPV006` | mixed nullable/non-nullable, unsupported types |
| LessThan | `LessThanAttribute` | numeric/decimal including nullable, default constructor | customized instances |
| LessThanOrEqualTo | `LessThanOrEqualToAttribute` | numeric/decimal including nullable, `int` threshold constructor with optional custom message | unsupported types, named arguments |
| MustBeGreaterThanOrEqual | `MustBeGreaterThanOrEqualAttribute` | same-type numeric/decimal/DateTime/DateTimeOffset including nullable pairs, compile-time `BPV007` | mixed nullable/non-nullable, unsupported types, named arguments |
| MustBeTrue | `MustBeTrueAttribute` | `bool` properties, optional custom message constructor | non-boolean types, named arguments |
| InFuture | `InFutureAttribute` | `DateTime`/`DateTimeOffset` including nullable pairs, optional custom message constructor | unsupported temporal types, named arguments |
| InPast | `InPastAttribute` | `DateTime`/`DateTimeOffset` including nullable pairs, optional custom message constructor | unsupported temporal types, named arguments |
| NotInFuture | `NotInFutureAttribute` | `DateTime`/`DateTimeOffset` including nullable pairs, optional custom message constructor | unsupported temporal types, named arguments |
| NotInPast | `NotInPastAttribute` | `DateTime`/`DateTimeOffset` including nullable pairs, optional custom message constructor | unsupported temporal types, named arguments |
| NotEmptyList | `NotEmptyListAttribute` | arrays and collection shapes with generated `Count`/`Length`, optional custom message constructor | enumerable-only shapes without deterministic count, named arguments |
| MaxItemsList | `MaxItemsListAttribute` | arrays and collection shapes with generated `Count`/`Length`, required max-items constructor with optional custom message | enumerable-only shapes without deterministic count, named arguments |
| RequiredIf | `RequiredIfAttribute` | scalar/null dependent values, default constructor, compile-time `BPV003` | arrays, custom messages, collection properties (`BPV017`) |
| RequiredIfNot | `RequiredIfNotAttribute` | scalar/null dependent values, default constructor, compile-time `BPV004` | arrays, custom messages, collection properties (`BPV017`) |
| RequiredItemsIf | `RequiredItemsIfAttribute` | collection shapes with generated `Count`/`Length` (falls back to runtime otherwise), scalar/null dependent values, compile-time `BPV015`; conditional, so absent from the OpenAPI schema | non-collection properties (`BPV018`), dependent-value arrays, custom messages |
| RequiredItemsIfNot | `RequiredItemsIfNotAttribute` | collection shapes with generated `Count`/`Length` (falls back to runtime otherwise), scalar/null dependent values, compile-time `BPV016`; conditional, so absent from the OpenAPI schema | non-collection properties (`BPV018`), dependent-value arrays, custom messages |
| NotAllowedIf | `NotAllowedIfAttribute` | scalar dependent values, default constructor, compile-time `BPV005` | null dependent values, arrays, custom messages |
| Password | `PasswordAttribute` | string properties, direct `IsValid` call, constructors with optional custom message/description flag | non-string types, named arguments |
| ResourceKey | `ResourceKeyAttribute` | string properties, direct `IsValid` call, optional custom message constructor | non-string types, named arguments |
| UkPostcode | `UkPostcodeAttribute` | string properties, direct `IsValid` call, optional custom message constructor | non-string types, named arguments |
| AllowedValues | `AllowedValuesAttribute` (.NET 8) | string/bool/int/long/double literal values in single `params` constructor | non-literal values, mixed types |
| DeniedValues | `DeniedValuesAttribute` (.NET 8) | string/bool/int/long/double literal values in single `params` constructor | non-literal values, mixed types |
| Length | `LengthAttribute` (.NET 8) | string properties, `Length(int, int)` constructor | non-string types (collections), custom error message |

When any single attribute on a property cannot be fast-pathed, only that property falls back to runtime validation — other properties with supported attributes remain fast-pathed.

### OpenAPI validation constraint projection

Validation attributes also project constraints into the OpenAPI specification at runtime through `OpenApiValidationConstraints.Extract(...)`:

| Attribute | OpenAPI constraint |
| --- | --- |
| `RequiredAttribute` | `required: true`; and on a request body's or schema component's string property that is not an enum, `minLength: 1` unless `AllowEmptyStrings` is set or a `StringLength`/`MinLength` gives the minimum, as legacy Blueprint's NJsonSchema documented it (never on a parameter) |
| `MaxLengthAttribute` | `maxLength` |
| `MinLengthAttribute` | `minLength` |
| `StringLengthAttribute` | `minLength` + `maxLength` |
| `LengthAttribute` (.NET 8) | `minLength` + `maxLength` |
| `RangeAttribute` | `minimum` + `maximum` (with `exclusiveMinimum`/`exclusiveMaximum` for exclusive bounds) |
| `GreaterThanAttribute` | `minimum` (with `exclusiveMinimum: true`) |
| `LessThanAttribute` | `maximum` (with `exclusiveMaximum: true`) |
| `RegularExpressionAttribute` | `pattern` |
| `EmailAddressAttribute` | `format: "email"` |
| `UrlAttribute` | `format: "uri"` |
| `PhoneAttribute` | `format: "phone"` |
| `AllowedValuesAttribute` (.NET 8) | `enum` (string values only) |

Format hints from validation attributes take precedence over CLR-type-derived format values (e.g. a `string` property with `[EmailAddress]` renders as `format: "email"` rather than no format).

## Diagnostic design rules

When adding new diagnostics:

1. Validate the contract at the earliest stage that has enough information.
2. Point at the most useful source location, not just the containing type.
3. Include concrete type, member, route, or configuration names in the message.
4. Make the fix path obvious from the message or description.
5. Choose severity intentionally. If generation would be wrong or unsafe, fail the build.

For migration diagnostics specifically:

6. Be explicit about whether the finding is auto-fixable, configuration-required, or manual-review-only.
7. Point to the relevant migration plan when the fix is not fully mechanical.
8. Prefer reports that can be consumed by both CI and AI agents without log scraping.

Good diagnostics here should help both humans and AI agents repair the issue quickly.

## Snapshot and cacheability tests are part of the harness

`TestHelpers.Compile<T>()` is not just a snapshot helper (`src\Blueprint.SourceGenerator.Tests\TestHelpers.cs`).

It does three important things:

- compiles the generated output to prove it is valid
- captures generator output for snapshot review
- reruns the incremental generator and asserts that the second run is fully cached

That means a generator change can be "textually correct" but still wrong if it breaks cacheability or emits unexpected diagnostics.

When adding a new validation rule, add or update the matching deterministic tests:

- unit tests for the diagnostic or helper behavior
- snapshot tests when generated output changes
- cacheability-sensitive tests when the generator pipeline changes
- runtime integration tests when the generated pipeline behavior changes and the runtime seam is executable in `Blueprint.SourceGenerator.Tests.Integration`

For pipeline contributor work, do not stop at a line-presence assertion in generated code. The default proof set is:

- a snapshot of the full generated pipeline shape
- compile-time failure coverage for invalid symbol-backed declarative contracts
- runtime integration coverage that executes the generated pipeline for both valid and invalid paths

Those tests should prove behavior through supported seams. Do not use reflection in tests to assert that a contract exists or to invoke private/internal helpers. If a behavior is hard to verify without doing that, improve the public or otherwise supported seam first and then test through that seam.

For new planning helpers and registries, do not stop at a single happy-path assertion. Cover:

- the expected happy path
- the failure or diagnostic path
- determinism edge cases such as duplicate producers, dependency cycles, ordering ties, or ambiguous metadata

## What should be validated at compile time

The preferred targets for future validation include:

- route-template placeholders and mapped resource properties
- marker-attribute arguments
- contributor composition rules such as duplicate unique contributors, missing contributor dependencies, and cyclic contributor ordering
- serializer and `JsonSerializerContext` completeness for API-facing contracts
- handler parameter resolution rules
- generated metadata or manifest completeness
- inspection-surface consistency for large APIs

If a feature depends on a string, type name, metadata shape, or serializer contract that is knowable during generation, the long-term goal is to validate it before the app runs.

The foundation module system now reserves `BPMOD001`-`BPMOD006` for contributor-composition and pipeline-planning diagnostics:

- `BPMOD001`: duplicate unique contributors (a contributor marked `IsUnique` appears more than once for the same capability).
- `BPMOD002`: missing contributor dependency (a contributor declares a dependency that no configured contributor can satisfy).
- `BPMOD003`: cyclic contributor dependency graphs (contributor dependencies form a cycle preventing deterministic ordering).
- `BPMOD004`: missing required pipeline variables discovered during pipeline-plan validation (a required variable is not provided or only becomes available after the consuming contributor's declared stage).
- `BPMOD005`: operation type does not exist in current compilation (a public planning seam was asked to build data for a missing type).
- `BPMOD006`: duplicate debug-manifest entry key (multiple contributors claim the same manifest section key).

These are compile-time failures because non-deterministic module composition or unresolved required pipeline variables would make generated output unstable and hard to inspect. `BPMOD004` is stage-aware: a matching provision must exist and be available no later than the consuming contributor's declared required stage. `BPMOD005` fails when a public planning seam is asked to build data for an operation type that does not exist in the current compilation. `BPMOD006` prevents multiple debug-manifest contributors from claiming the same entry key so inspection artifacts stay deterministic. Recent pipeline-contract cleanup intentionally removed the separate orphan-render-step diagnostic by making stage participation implicit in the planning helpers instead of leaving it as caller-managed bookkeeping.

## Human- and AI-friendly diagnostics

Diagnostics in this repo should be:

- stable, with meaningful IDs
- precise about the failing contract
- easy to correlate back to source and generated output
- specific enough that a tool can act on them without guesswork

Avoid vague messages like "invalid configuration" when the build can say "property `UserId` does not exist on operation `CreateUserCommand`".

## Preferred escalation path

When a repeated class of bug appears:

1. first ask whether a diagnostic should exist
2. then ask whether a test should prove it
3. then update `/docs` so the principle and harness are both documented

The repository should become harder to misuse over time.

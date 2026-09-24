# Architecture

This repository is built around a compile-time pipeline. The runtime should execute precomputed knowledge, not discover the shape of the application on the fly.

## Layer map

| Layer | Responsibility | Key files |
| --- | --- | --- |
| `Blueprint.Runtime.Model` | Contracts and metadata types that define the public shape of operations, descriptors, links, and the app registry. | `src\Blueprint.Runtime.Model\IOperation.cs`, `ApiOperationDescriptor.cs`, `BlueprintApp.cs` |
| `Blueprint.SourceGenerator` | Incremental generators that discover resources, operations, and apps, validate them, and emit resource helpers, descriptors, registrations, pipelines, and executors. | `src\Blueprint.SourceGenerator\Generators\ApiResources\ApiResourceSourceGenerator.cs`, `Generators\Descriptors\OperationDescriptorSourceGenerator.cs`, `Generators\App\AppDataModelSourceGenerator.cs`, `Generators\Pipelines\OperationExecutorPipelineSourceGenerator.cs` |
| `Blueprint.Runtime` | The runtime execution layer that uses generated metadata and generated pipelines to execute operations and map HTTP endpoints. | `src\Blueprint.Runtime\Exec\BlueprintAppDataModel.cs`, `Http\ApplicationBuilderExtensions.cs`, `Diagnostics\BlueprintActivitySource.cs` |
| Samples and tests | Reference consumers plus deterministic harnesses for generator output and runtime integration. | `src\Blueprint.SourceGenerator.Sample`, `src\Blueprint.SourceGenerator.Sample.Contract`, `src\Blueprint.SourceGenerator.Tests`, `src\Blueprint.SourceGenerator.Tests.Integration` |

## Core compile-time flow

### 1. Operation contracts define the shape

Operations implement `IOperation`, usually through `ICommand` or `IQuery`, and must expose `static ApiOperationDescriptor Configure()` (`src\Blueprint.Runtime.Model\IOperation.cs`).

That decision is important: operation metadata is part of the compile-time contract, not a runtime convention.

### 2. Resource generation fills in API resource metadata

`ApiResourceSourceGenerator` scans `ApiResource` subclasses, requires partial types when it needs to synthesize the `Object` property, and emits `*_ObjectProperty.g.cs` partials (`src\Blueprint.SourceGenerator\Generators\ApiResources\ApiResourceSourceGenerator.cs`).

This generator is separate from the descriptor/module pipeline, but it follows the same architectural rule: resource metadata that can be derived at build time should be generated at build time.

### 3. Descriptor generation builds operation metadata

`OperationDescriptorSourceGenerator`:

- injects marker attributes during post-initialization
- finds operation classes
- validates required structure
- lets modules enrich the model
- emits `*_Descriptor.g.cs` partials that provide `Configure()`

Key files:

- `src\Blueprint.SourceGenerator\Generators\Descriptors\OperationDescriptorSourceGenerator.cs`
- `src\Blueprint.SourceGenerator\Modules\IBlueprintSourceGeneratorModule.cs`
- `src\Blueprint.SourceGenerator\Modules\LinkGeneration\LinkGenerationModule.cs`

This stage is where string-based metadata should be turned into symbol-backed, validated configuration.

### 4. App generation wires operations into a registry

`AppDataModelSourceGenerator` scans `[BlueprintAppScan]` types and emits `*_DataModelRegistration.g.cs` partials that call `this.Register(Operation.Configure())` (`src\Blueprint.SourceGenerator\Generators\App\AppDataModelSourceGenerator.cs`).

App roots may now also declare `[BlueprintAppSerializerContext(JsonSerializerContextType = typeof(...))]`. The generator
parses that app-owned serializer context declaration once and carries the declared serializer-context instance, the
declared serializer-context type, and both the typed and string-backed `JsonSerializable(...)` body-type inventories into
`BlueprintApp.AppOwnedSerializerContext`, `BlueprintApp.AppOwnedSerializerContextType`,
`BlueprintApp.AppOwnedSerializerContextBodyTypes`, and `BlueprintApp.AppOwnedSerializerContextBodyTypeNames` through the
generated registration constructor. That gives later OpenAPI coverage-validation and schema slices a consumer-owned JSON
metadata seam without routing through runtime host options first.

App roots may also declare `[BlueprintAppOperationNames(Remove = "Portal")]`, which removes every ordinal occurrence of
that text from the name of each operation the app hosts, after `Query`/`Command` suffix stripping — so
`PortalResetActiveUsersCommand` is named `ResetActiveUsers`. It replaces legacy Blueprint's per-app
`IOperationScannerConvention` and keeps its semantics. The rename cannot live in the descriptor, because descriptors are
generated with the operation, often in a contract assembly that cannot see which app hosts it, and one operation can be
hosted by several apps. Instead the registration constructor emits `this.Register(ApplyOperationNameTransform(Operation.Configure()))`:
each `Configure()` call returns a fresh descriptor, so the app renames only its own instances before `Freeze()`. Every
reader of `ApiOperationDescriptor.Name` in that app — OpenAPI `operationId` values, request-body schema keys, `x-links`,
and code reading `link.OperationDescriptor.Name` such as an authorisation-manifest query — then sees the app's name.
Resource and response schema names, which come from CLR types, do not change, and nor do the compile-time manifest
artifacts, which are keyed by operation type. `BP011` fails the build when the removal gives two operations the same name,
or leaves one with an empty name.

`HttpOperationResultExecutor` now also consumes that same app-owned serializer seam for HTTP API responses. When the app
declares a `JsonSerializerContext`, the executor prefers its generated type metadata for semantic result bodies and
problem responses; uncovered fallback bodies still serialize through `System.Text.Json` using the context's options when
available rather than routing back through `Newtonsoft.Json`.

The supported semantic HTTP surface now also includes `Blueprint.Runtime.Exec.ResourceEventResult<T>`, which keeps
body-bearing resource-event responses distinct from ordinary `CreatedResult<T>` flows. The current HTTP host translates
that family to `201 Created` JSON envelopes with top-level `eventId`, `resourceId`, and `content`, so later same-status
union and OpenAPI slices can reason from a stable serialized discriminator instead of an implicit payload convention.
Consumers can now declare `[ResourceEvent("...")]` on the payload type carried by `ResourceEventResult<T>`, and
descriptor generation projects that compile-time proof into `OperationResourceEventMetadataFeatureData` so later
discriminator/OpenAPI steps do not need to infer event identities from runtime-only constructor arguments.
When an HTTP-visible same-status union is made entirely of those proven body-bearing resource-event outcomes, the
OpenAPI renderer now emits a `oneOf` resource-event envelope with top-level `eventId`, `resourceId`, and `content`
plus discriminator metadata on `eventId`, while unrelated same-status unions still remain on the diagnostic path.

The runtime model now follows that same serializer contract end to end: `Blueprint.Model.ApiResource` and
`Blueprint.Model.Link` rely only on `System.Text.Json` attributes, so `$object`, `$links`, `$partial`, and null-link
omission behavior no longer depend on leftover `Newtonsoft.Json` annotations in `Blueprint.Runtime.Model`.

`OpenApiSchemaProjectionContext` now also fails fast when a covered serializer-backed body type uses cross-property
validation attributes that the current local property-constraint model cannot represent, including
`CompareAttribute` and `GreaterThanPropertyAttribute`. `AppDataModelSourceGenerator` mirrors that same rule earlier as
`BP008` whenever an app-owned serializer context already proves the unsupported body type is part of the HTTP-visible
contract, so consumers learn about that OpenAPI gap during generation rather than only when runtime schema projection
executes.

That same generator now also reuses the shared descriptor-planning seam (`src\Blueprint.SourceGenerator\Planning\DescriptorPlanBuilder.cs`) before it emits registrations. That keeps app-level serializer coverage validation (`BP006`) and missing-declaration validation (`BP007`) on the same contributor ordering, host-exposure metadata, and exception-translation policy used by `OperationDescriptorSourceGenerator`, rather than duplicating HTTP/result/body-shape analysis in a separate app-only code path.

The generated app registration means the runtime registry is built from generated descriptors rather than startup-time discovery logic.

### 5. Pipeline generation binds handlers and execution

`OperationExecutorPipelineSourceGenerator`:

- finds Blueprint app roots
- combines them with the compilation
- discovers handlers
- emits per-operation `*ExecutorPipeline.g.cs`
- emits app-specific `*Executor.g.cs`

Key files:

- `src\Blueprint.SourceGenerator\Generators\Pipelines\OperationExecutorPipelineSourceGenerator.cs`
- `src\Blueprint.SourceGenerator\Generators\Pipelines\HandlerDiscovery.cs`
- `src\Blueprint.SourceGenerator\Generators\Pipelines\ParameterAnalyzer.cs`
- `src\Blueprint.SourceGenerator\Generators\Pipelines\CodeGen\PipelineCodeGenerator.cs`
- `src\Blueprint.SourceGenerator\Generators\Pipelines\CodeGen\ExecutorCodeGenerator.cs`

`ParameterAnalyzer` is the bridge between handler signatures and runtime behavior. It decides which parameters come from the operation, the context, context properties, or DI.

The outer executor-shell exception path is now also ordered explicitly through `PipelineExceptionTranslatorPolicy`, which is composed from ordered `IExceptionTranslationContributor` registrations so built-in and companion-package `IPipelineExceptionTranslator` catch blocks stay deterministic and ambiguous non-fallback overlap is rejected before the generator emits the catch shell.

### 6. Runtime executes precomputed metadata

At runtime:

- `BlueprintApp` stores registered operations and links (`src\Blueprint.Runtime.Model\BlueprintApp.cs`)
- `BlueprintAppExecutor` uses generated pipeline registrations (`src\Blueprint.Runtime\Exec\BlueprintAppDataModel.cs`)
- `MapBlueprintApi<TApp, TExec>()` maps the generated surface into ASP.NET Core endpoints (`src\Blueprint.Runtime\Http\ApplicationBuilderExtensions.cs`)
- `OpenApiDocumentBuilderContext.Create(app)` snapshots the app-owned serializer declaration plus descriptor-published HTTP response and serializer-coverage metadata for HTTP-exposed operations so later OpenAPI document builders can consume one stable runtime seam (`src\Blueprint.Runtime\Http\OpenApiDocumentBuilderContext.cs`)
- `OpenApiSchemaProjectionContext.Create(document)` projects the covered serializer-backed body payload types from that document-builder seam into stable `OpenApiSchemaModel` entries with broad schema kind plus ordered property-name, property-kind, requiredness, extension-data, and supported validation/data-annotation constraint facts. Because source-generated `JsonPropertyInfo.AttributeProvider` is currently null on this path, the runtime falls back to covered CLR property attributes mapped onto the serializer-exposed JSON property names before projecting those constraint facts (`src\Blueprint.Runtime\Http\OpenApiSchemaProjectionContext.cs`)
- `OpenApiResponseSchemaReferenceProjectionContext.Create(schemaProjection)` merges the current HTTP response metadata with the covered schema inventory so later OpenAPI document builders can consume stable per-response schema references without recomputing body-type joins (`src\Blueprint.Runtime\Http\OpenApiResponseSchemaReferenceProjectionContext.cs`)
- `OpenApiSchemaComponentRenderingContext.Create(schemaProjection)` renders the covered schema inventory into reusable component-shaped runtime models with OpenAPI-style type names, ordered required-property names, and ordered rendered property entries, including the currently supported schema constraint keywords for length, pattern, and numeric bounds, while still leaving response, operation, and document assembly to later slices (`src\Blueprint.Runtime\Http\OpenApiSchemaComponentRenderingContext.cs`)
- `OpenApiResponseObjectRenderingContext.Create(responseSchemaProjection)` then binds the current HTTP response families onto those reusable schema components, preserving per-response status/body/result metadata while adding deterministic JSON response content only for covered schema components and still leaving operation/document assembly to later slices (`src\Blueprint.Runtime\Http\OpenApiResponseObjectRenderingContext.cs`)
- `OpenApiOperationRenderingContext.Create(responseObjects)` then binds descriptor-published operation identity, ordered HTTP methods, and ordinal-stable URL formats onto those rendered response objects (`src\Blueprint.Runtime\Http\OpenApiOperationRenderingContext.cs`)
- `OpenApiPathItemRenderingContext.Create(operationRendering)` then groups those operation models into deterministic path-item contracts keyed by route template and HTTP method, leaving only final document emission to later slices (`src\Blueprint.Runtime\Http\OpenApiPathItemRenderingContext.cs`)
- `OpenApiDocumentRenderingContext.Create(pathItemRendering)` then assembles the deterministic path-item inventory and reusable schema-component inventory into one host-facing document model, leaving only output rendering and host exposure to later slices (`src\Blueprint.Runtime\Http\OpenApiDocumentRenderingContext.cs`)
- `OpenApiOutputRenderingContext.Create(document, infoOptions, schemaProcessorOptions)` then renders the final JSON document and applies any ordered runtime schema processors to each `OpenApiSchemaComponentModel` immediately before emitting `components/schemas`. That output seam now also writes the projected OpenAPI constraint keywords (`minLength`, `maxLength`, `pattern`, `minimum`, `maximum`, `exclusiveMinimum`, `exclusiveMaximum`) so consumer-owned schema polish stays in the output seam instead of widening schema projection or descriptor generation (`src\Blueprint.Runtime\Http\OpenApiOutputRenderingContext.cs`)
- `MapBlueprintApi<TApp, TExec>(..., configureOpenApi, configureOpenApiSchemas)` threads those optional OpenAPI output customisation hooks through the hosted document endpoint while keeping the document pre-rendered once at startup (`src\Blueprint.Runtime\Http\ApplicationBuilderExtensions.cs`)

The important architectural rule is that runtime code consumes generated knowledge. It should not re-scan the compilation model or re-derive contracts that the generators already knew.

### Supported HTTP result types

`HttpOperationResultExecutor` translates semantic `OperationResult` subtypes to HTTP responses:

| Result type | HTTP status | Body | Notes |
| --- | --- | --- | --- |
| `OkResult<T>` | 200 | JSON | Standard success with payload |
| `CreatedResult<T>` | 201 | JSON | + Location header from self-link |
| `AcceptedResult<T>` | 202 | JSON | Async accepted |
| `NotFoundResult<T>` | 404 | JSON | Missing resource |
| `ConflictResult<T>` | 409 | JSON | State conflict |
| `ResourceEventResult<T>` | 200/201/204 | JSON envelope, none on 204 | Status from ChangeType; Created adds Location header |
| `KeyOnlyResourceEventResult` | 200/201/204 | None | Status from ChangeType |
| `NoResultOperationResult` | 200 | None | Default void success |
| `HttpNoContentResult` | 204 | None | Explicit no-content |
| `PlainTextResult` | 200 (configurable) | text/plain, none on 204/205/304 | Plain-text body (e.g. health checks) |
| `RedirectResult` | 302 (configurable) | None | Location header redirect |
| `FileStreamResult` | 200 | Stream | Binary download; Content-Disposition when FileName set |
| `ValidationFailedResult` | 422 | ProblemDetails | Validation errors |
| `UnhandledExceptionResult` | 500 | ProblemDetails | Internal errors |
| `UnauthenticatedResult` | 401 | None | Identity required |
| `ForbiddenResult` | 403 | None | Permission denied |
| `BindingFailureResult` | 400 | None | Request binding failed |
| `UnsupportedMediaTypeResult` | 415 | None | Wrong content type |
| `StatusCodeResult.*` | Various | None | Bodyless status code singletons (200, 201, 202, 204, 400, 501, 503) |
| `NotModifiedResult<T>` | 304 | None | Conditional request matched (ETag/If-None-Match via `ETagger`) |

## Modules are the extension seam

`IBlueprintSourceGeneratorModule` is the main extension point for new compile-time features (`src\Blueprint.SourceGenerator\Modules\IBlueprintSourceGeneratorModule.cs`).

Modules participate in the build through typed contributor interfaces, each with its own lifecycle stage:

| Interface | Stage | Responsibility |
| --- | --- | --- |
| `IPostInitializationContributor` | Source injection | Inject marker attributes and shared types into the compilation. |
| `IDescriptorContributor` | Descriptor planning | Validate contracts, project metadata into descriptors, emit feature-data code. |
| `IPipelineContributor` | Pipeline planning | Register variables, render pipeline stages (pre/post execution, exception handling). |
| `IHostContributor` | Host exposure | Declare HTTP, background-task, or custom host exposure per operation. |
| `IExceptionTranslationContributor` | Exception shell | Contribute catch blocks to the generated executor's outer exception handler. |
| `IDebugManifestContributor` | Manifest emission | Project structured diagnostic data into the generated debug manifest. |
| `IAppRegistrationContributor` | App registration | Contribute to the generated app registration code. |

A module typically implements one or two contributors. For example:

- `LinkGenerationModule` implements `IPostInitializationContributor` (injects link attributes) and `IDescriptorContributor` (validates and emits link metadata)
- `ValidationModule` implements `IPipelineContributor` (renders validation checks before handler execution)
- `AuditingModule` implements `IPipelineContributor` (renders post-execution audit sink calls)
- `AuthenticationModule` implements `IDescriptorContributor` (projects auth metadata) and `IPipelineContributor` (renders identity checks)
- `OperationDiscoveryManifestModule` implements `IDebugManifestContributor` (emits per-operation discovery entries)

The repository currently has 44 module classes across 19 capability areas covering validation, persistence, auditing, authentication, authorization, performance metrics, link generation, resource events, resource expansion, property ownership, background tasks, messaging, OpenAPI metadata, debug manifests, and host dispatch.

If a new feature needs compile-time metadata or diagnostics, adding or extending a module is usually the right place to start.

Not every generator concern needs to flow through a module. `ApiResourceSourceGenerator` is a good example of a standalone generator that owns a separate compile-time concern cleanly.

## Incremental generation is part of the architecture

The generators use tracked stages such as `InitialExtraction`, `CombineWithCompilation`, `Configuration`, and `FindHandlers` (`WithTrackingName(...)` in the generator entry points).

That is not just instrumentation noise. It supports the repository's expectation that generators remain incremental and cacheable.

`TestHelpers.Compile<T>()` runs the generator twice and asserts that the second run is fully cached (`src\Blueprint.SourceGenerator.Tests\TestHelpers.cs`).

When changing generator behavior, preserve both correctness and cacheability.

## Planned target architecture

The current implementation is intentionally smaller than the target architecture described in `/migration`.

The future-state direction is:

- modules contribute to explicit planning models, not directly to ad-hoc generated strings
- descriptor, pipeline, host, registration, and manifest contributions stay separate but coordinated
- a thin shared executor shell owns outer scaffolding while per-operation plans own operation-specific behavior
- generated manifests and inspection surfaces explain the built API shape for humans, CI, and AI agents

The first Phase 0 foundation slice now exists in the current implementation:

- `BlueprintSourceGenerationConfig.BuildExecutionPlan()` resolves ordered contributors and validates composition with deterministic diagnostics (`BPMOD001`-`BPMOD003`)
- `BlueprintSourceGenerationConfig.BuildPipelinePlan(...)` is the supported seam for constructing and validating a per-operation `PipelinePlan` outside private generator helpers
- `LinkGenerationModule` is now the first real post-initialization and descriptor contributor routed through the execution plan instead of being hard-coded directly into descriptor generation
- initial planning models now exist for `OperationModel`, `DescriptorPlan`, `PipelinePlan`, `AppRegistrationPlan`, `GeneratedDebugManifest`, and `VariableRegistry`
- `IPipelineContributor` now exposes a typed `ContributeToPipeline(PipelineContributorContext)` seam, descriptor contributors use `ContributeToDescriptor(...)`, descriptor generation runs `ConfigurePipelinePlan()` to build pipeline planning state and report `BPMOD004` for required variables that are missing or only become available after the consuming contributor's declared stage, planning context classes now expose helper methods plus a small safe read-only metadata surface instead of record-shaped bags of mutable inner state, and the executor-pipeline generator now activates that seam for validation through `ValidationModule` and handler execution through `HandlerExecutionModule`

At this stage post-initialization, descriptor, and pipeline planning/validation seams are executed end-to-end in descriptor generation, and the executor-pipeline generator routes validation-stage DataAnnotations/IValidatableObject checks through `ValidationModule` plus execution-stage handler invocation through `HandlerExecutionModule` while preserving the previous validation catch/result behavior. `HandlerExecutionModule` now statically normalizes handler returns into the current semantic runtime-result shapes: plain non-null values become typed `Blueprint.Runtime.Exec.OkResult<T>`, `void`/`Task` becomes `Blueprint.Runtime.Exec.NoResultOperationResult`, explicit `Blueprint.OperationResult` returns pass through unchanged, and the first `Results<T1, T2>` union contract unwraps directly to its concrete member result without runtime type probing in the hot path. `ValidationModule` now plans property validation independently: supported property-level DataAnnotations families (`Required`, `MaxLength`, `MinLength`, `StringLength`, and `EmailAddress`) render generated fast-path checks through extracted strategy helpers, unsupported property shapes use the shared `Blueprint.Validation.ValidationRuntime.ValidateProperty(...)` fallback against `ApiOperationDescriptor.GetPropertyAttributes(...)`, and type-level validation attributes plus `IValidatableObject` remain on the existing whole-object `Validator.TryValidateObject(...)` fallback path for now. Generator-owned validation short-circuits now return canonical `Blueprint.Runtime.Exec.ValidationFailedResult`, the legacy `Blueprint.Middleware.ValidationFailedOperationResult` name stays available only as a compatibility alias, and the HTTP host now translates that failure family to `422 Unprocessable Entity` problem responses when the descriptor advertises matching `OperationFailureOutcomesFeatureData`. The shared outer exception shell now constructs canonical `Blueprint.Runtime.Exec.UnhandledExceptionResult`, while the legacy `Blueprint.UnhandledExceptionOperationResult` name remains only as a compatibility alias; both stay internal-only fallback shapes and are rejected from declared public result contracts even though the HTTP host can now translate descriptor-advertised unhandled fallback results to `500 Internal Server Error` problem responses at runtime. That outer shell is no longer monolithic, though: `PipelineCodeGenerator` now delegates catch-block emission to `IPipelineExceptionTranslator`, the validated `PipelineExceptionTranslatorPolicy` keeps specific translators ordered ahead of the catch-all fallback, `DefaultExceptionTranslationModule` contributes the built-in translators through the same execution-plan contributor surface that companion packages now extend, and both descriptor and pipeline generators resolve that shared policy from the ordered exception-translation contributors instead of recreating hard-coded defaults independently. The built-in contributors still preserve today's validation-exception handling, non-5xx `ApiException` logging path, and generic unhandled fallback so later error-model slices can widen exception-translation policy without reworking the shared executor shell. Pipeline render steps now carry contributor-owned rendering callbacks with no separate behavior-driving enum, stage participation is inferred implicitly when contributors register render steps or variables, and descriptor/pipeline/render contexts expose helper methods plus only the small read-only metadata properties needed for clear module code instead of raw public access to mutable plan, handler, or writer objects. `PipelineCodeGenerator` only replays ordered steps instead of interpreting validation or handler-execution markers centrally. Logging and app-registration contributors are still deliberately scaffolded or hard-coded so later migration slices can fill them in without reinventing the foundation contract, but host validation is no longer entirely dormant: `LinkGenerationModule` is now the first live HTTP `IEnabledHostContributor`, `TasksHostModule` is the first core-owned non-HTTP follow-on driven by the `Blueprint.Tasks.IBackgroundTask` marker, and descriptor/debug-manifest planning validates `HostExposureRegistration` metadata against the enabled host-contributor set before descriptor or manifest projection continues. Unsupported host exposure fails deterministically with `BPOD001`, while operations that land on more than one enabled host now fail with `BPOD002`, keeping the enabled-host registry as the single compile-time source of truth for host ownership. The tasks-host slice remains intentionally narrower than the later background-task workstream: it keeps host-specific intent explicit at compile time and makes those host-conflict rules provable without pulling scheduler/runtime task-host behavior forward before that workstream is ready. Later non-HTTP hosts may also live in the core Blueprint package when they represent stable reusable product capability, but their namespaces and public type names should stay obviously host-specific so HTTP, messaging, and other host surfaces remain easy to distinguish. The debug-manifest seam is now activated through `BlueprintSourceGenerationConfig.BuildDebugManifest(...)`, `DebugManifestContributorContext`, `OperationDiscoveryManifestModule`, and `OperationsDiscoveryManifestEmitter` for the first operations/discovery payload, and debug-manifest entry keys are now contributor-owned too: when a later contributor reuses a claimed key, `BuildDebugManifest(...)` fails deterministically with `BPMOD006` instead of silently replacing the earlier entry. The same `OperationDiscoveryManifestModule` now also seeds generated descriptors with typed `OperationDiscoveryFeatureData` carrying the approved `kind`, `namespace`, and `typeName` facts, so later runtime and Phase 1 contributors can read those discovery facts from `ApiOperationDescriptor` without rediscovering them separately. HTTP-exposed operations now also project additive `OperationGroupMetadataFeatureData`: `OperationGroupMetadataModule` prefers an explicit core `[ApiOperations("...")]` declaration when one is present on the operation type and otherwise falls back to the last discovery-namespace segment, so later OpenAPI slices can consume stable tag/grouping metadata while the broader grouped-handler discovery story keeps moving into core. Declared semantic result contracts now also project additive `OperationResultOutcomesFeatureData`: `Results<T1, T2>` contracts flatten into ordered `OperationResultOutcome` entries, single declared semantic result types surface as a one-outcome descriptor feature, and contract-only compilations that do not reference `Blueprint.OperationResult` skip that feature instead of failing generation. Ordered exception ownership now projects additive `OperationFailureOutcomesFeatureData` too: `FailureOutcomesModule` reuses the same validated contributor-composed translator policy to publish ordered `OperationFailureOutcome` entries carrying the stable translator identifier, handled exception type name, and translated result type name for each known failure family, whether the translator came from Blueprint's default module set or a companion package module. That failure metadata intentionally stays host agnostic and string-based: contract-only compilations can keep the descriptor feature data without taking a hard dependency on runtime-only failure CLR types, while the HTTP host still reads those stable result-type names at runtime instead of freezing another parallel exception/result registry. `HttpResponseMetadataModule` now composes those ordered failure families with flattened semantic success outcomes for HTTP-exposed operations, publishing additive `OperationHttpResponseMetadataFeatureData` entries keyed by status code and carrying the current response-body flag, contributing semantic result type names, and any compile-time-known body payload type names. Typed semantic results contribute their payload CLR type names through that seam, while the built-in validation and unhandled fallback families contribute the current host-owned `Microsoft.AspNetCore.Mvc.ValidationProblemDetails` and `Microsoft.AspNetCore.Mvc.ProblemDetails` body contracts. `SerializerContextCoverageModule` now builds directly on that response metadata instead of touching runtime execution: it projects additive `OperationSerializerContextCoverageFeatureData` containing the distinct body payload CLR type names later serializer-context validation and OpenAPI schema slices must account for, making serializer requirements inspectable while the current HTTP host still serializes through `Newtonsoft.Json`. The app-registration seam now carries the missing ownership contract too: `[BlueprintAppSerializerContext(JsonSerializerContextType = typeof(...))]` on a Blueprint app is parsed once by `AppDataModelSourceGenerator`, generated app constructors assign that app-owned type to `BlueprintApp.AppOwnedSerializerContextType`, and when the declaration includes `JsonSerializable(...)` attributes they also persist the declared body payload CLR type inventory to `BlueprintApp.AppOwnedSerializerContextBodyTypeNames`. Both the sample consumer and integration app now declare contexts through that seam. Later OpenAPI/schema slices can therefore compare descriptor-published coverage inventory against a consumer-owned serializer-context declaration and its stable declared body-type inventory instead of rediscovering executor policy or routing through `BlueprintHttpOptions`. The first runtime OpenAPI seam now builds directly on that contract too: `Blueprint.Runtime.Http.OpenApiDocumentBuilderContext.Create(app)` snapshots the app-owned serializer declaration plus per-operation `OperationHttpResponseMetadataFeatureData`, `OperationSerializerContextCoverageFeatureData`, and `OperationGroupMetadataFeatureData` for HTTP-exposed operations only. Later document builders can therefore consume one deterministic HTTP-host context instead of re-deriving serializer coverage from executor behavior, route scans, or ad-hoc option state. `Blueprint.Runtime.Http.OpenApiSchemaProjectionContext.Create(document)` then projects the covered serializer-backed body payload types into stable `OpenApiSchemaModel` entries, now including ordered `OpenApiSchemaPropertyModel` inventories with broad property kind plus serializer-owned requiredness and extension-data facts while preserving the earlier `PropertyNames` compatibility surface, `Blueprint.Runtime.Http.OpenApiResponseSchemaReferenceProjectionContext.Create(schemaProjection)` resolves each response family's covered `BodyTypeNames` onto that projected schema inventory without widening yet into document rendering, `Blueprint.Runtime.Http.OpenApiSchemaComponentRenderingContext.Create(schemaProjection)` now renders those projected schemas into reusable `OpenApiSchemaComponentModel` entries with OpenAPI-style type names, ordered required-property names, and ordered rendered property contracts, `Blueprint.Runtime.Http.OpenApiResponseObjectRenderingContext.Create(responseSchemaProjection)` now binds the current HTTP response families onto those reusable components through deterministic JSON response-content entries that reference the reusable rendered schema components while still leaving operation assembly deferred, and `Blueprint.Runtime.Http.OpenApiOperationRenderingContext.Create(responseObjects)` now binds descriptor-published operation identity, ordinal-stable URL formats, and operation-group-derived `Tags` onto those response objects while still leaving HTTP method projection, path grouping, and full document assembly for later slices. `FlattenedResultOutcomeModule` now also keeps descriptor result metadata honest: `BP111` rejects `Results<T1, T2>` contracts whose members would force flattened descriptor outcomes away from the declared semantic-result set, such as plain CLR payload members or nested unions, instead of quietly projecting partial or non-semantic outcome data. Host-exposed operations now also fail fast at descriptor time when declared semantic result contracts outrun the currently enabled HTTP executor surface: `HostExecutorAvailabilityModule` reuses the flattened semantic-result analysis plus `HostExposureRegistration` metadata to report `BP108` whenever a declared result type has no current HTTP executor or adapter coverage. A second descriptor-time guard now rejects the first class of same-host ambiguity too: `AmbiguousHostTranslationModule` reports `BP109` when an HTTP-exposed `Results<T1, T2>` contract contains multiple members that the current HTTP executor would translate to the same response shape, such as multiple `OkResult<T>` members that all collapse to `200` JSON responses. `NoResultShapeSeparationModule` now adds the next semantic guard: `BP112` reports when an HTTP-exposed `Results<T1, T2>` contract mixes host-neutral `NoResultOperationResult` with host-owned no-content/status-only result families such as `Blueprint.Runtime.Http.HttpNoContentResult`, so descriptor generation does not quietly flatten two distinct no-body concepts into one bucket. `HttpUnionDiscriminationStrategyModule` still owns same-status client discrimination: `BP110` reports when supported HTTP-visible union members share one status code without an explicit host/client discrimination strategy, such as `OkResult<T>` plus `NoResultOperationResult` both mapping to `200 OK`. The current runtime migration path away from self-executing results is now host-owned but intentionally partial: `MapBlueprintApi(...)` routes supported HTTP success shapes through `Blueprint.Runtime.Http.HttpOperationResultExecutor`, translating `OkResult<T>` payloads to JSON `200 OK` responses, the host-neutral `Blueprint.Runtime.Exec.CreatedResult<T>` semantic shape to JSON `201 Created` responses, the host-neutral `Blueprint.Runtime.Exec.AcceptedResult<T>` semantic shape to JSON `202 Accepted` responses, the host-neutral `Blueprint.Runtime.Exec.NotFoundResult<T>` semantic shape to JSON `404 Not Found` responses, the host-neutral `Blueprint.Runtime.Exec.ConflictResult<T>` semantic shape to JSON `409 Conflict` responses, descriptor-advertised `ValidationFailedResult` families to `422 Unprocessable Entity` problem responses, descriptor-advertised `UnhandledExceptionResult` families to `500 Internal Server Error` problem responses, host-neutral `NoResultOperationResult` to empty `200 OK` responses, the host-neutral `Blueprint.Runtime.Exec.KeyOnlyResourceEventResult` semantic shape to empty `201 Created` responses by convention, and the first explicit host-owned no-content result `Blueprint.Runtime.Http.HttpNoContentResult` to empty `204 No Content` responses. That split is deliberate: `CreatedResult<T>` is the core semantic "resource created with a response payload" shape, `AcceptedResult<T>` is the core semantic "accepted but not yet completed" success shape, `NotFoundResult<T>` is the core semantic "missing resource with a response payload" shape, `ConflictResult<T>` is the core semantic "state or domain conflict with a response payload" shape, `ValidationFailedResult` is now the canonical semantic validation-failure name and part of the supported HTTP executor surface, `UnhandledExceptionResult` is now the canonical internal fallback shell while `Blueprint.UnhandledExceptionOperationResult` survives only as a compatibility alias and neither is valid as a declared public semantic result contract, `NoResultOperationResult` remains the core semantic "success with nothing meaningful to return" shape, `KeyOnlyResourceEventResult` remains the host-neutral "resource-event completed with identity but no body" shape, and transport-shaped conveniences such as explicit no-content, status-only, or plain-text HTTP responses stay in host namespaces/packages instead of widening the core result taxonomy. Unsupported legacy results still fall back to direct `OperationResult.ExecuteAsync(...)` so later host-translation slices can retire that path deliberately instead of by accident. URL-bearing operations now also carry typed `OperationHostExposureFeatureData`, with host contributors such as `LinkGenerationModule` and `TasksHostModule` recording shared host-exposure planning metadata and `BlueprintApp.GetOperationsForHost(...)` indexing registered descriptors from that compile-time metadata instead of rediscovering URLs or task markers at runtime. The operations manifest now reuses that same descriptor-planning metadata through `DebugManifestContributorContext.GetMetadata<T>()`, so inspection surfaces expose ordered per-operation `hostExposure` arrays without reparsing link attributes or host-marker interfaces separately. Richer manifest families and runtime projections remain follow-on work.

The first shared manifest contract-definition pass now also exists in `src\Blueprint.SourceGenerator\Artifacts\GeneratedManifestEnvelope.cs`. Build-time manifest producers currently default to the `.blueprint/manifests/<artifact>.manifest.json` layout, carry shared app/generator identity fields, and extend the schema through contributor-owned `sections` plus uniquely keyed `operations` entries so later observability and review surfaces can project from one deterministic envelope. The first real manifest contributor now projects operations/discovery summaries through that contract, using the `operations-discovery` section for contributor-owned summary metadata and canonical `operations` entries for the per-operation view, and the first derived review projection now comes from `GeneratedManifestEnvelope.ToMarkdown()` so humans and AI tools can read the same envelope data without rediscovering the app shape. The debug-manifest planning layer now applies that same ownership idea within each per-operation payload: entry keys belong to the contributor that claims them first, and cross-contributor collisions fail with `BPMOD006` so future manifest enrichments cannot silently overwrite one another. The first runtime inspection seam now loads that same JSON from embedded resources through `Blueprint.Runtime.Artifacts.ManifestResourceLoader` plus `GeneratedManifestEnvelope.FromJson(...)`, and the current hosted follow-ons map development-only `/.blueprint/manifests/operations.json` and `/.blueprint/manifests/operations.html` routes beneath `MapBlueprintApi(...)` when `BlueprintHttpOptions.OperationsManifestResourceName` is configured. The JSON route still serves the canonical manifest bytes directly, while the HTML page fetches that JSON client-side instead of teaching `Blueprint.Runtime` a second manifest interpretation layer. The next widening keeps that same rule: `/.blueprint/manifests/operations.md` may be hosted from `BlueprintHttpOptions.OperationsManifestMarkdownResourceName`, but it serves prebuilt markdown bytes exactly as embedded rather than projecting markdown from JSON inside the runtime assembly. That keeps `Blueprint.Runtime` free of a project reference back to `Blueprint.SourceGenerator`, and richer hosted markdown/tooling projections remain a deliberate later boundary decision. That current discovery shape is the approved starting point, not a promise that every later debug-manifest surface must keep the exact same payload forever.

Contributor contract conventions:

- concrete module implementations end with `Module`
- concrete module files live in dedicated subfolders under `src\Blueprint.SourceGenerator\Modules`
- module-specific helper files live beside the owning module inside that folder
- contributor methods are named for the seam they populate (`ContributeToDescriptor`, `ContributeToPipeline`, etc.)
- planning context classes expose helper methods plus only a small read-only metadata surface; they do not publish mutable inner plan, handler, or writer objects as public state

Representative planned models:

- `OperationModel`
- `DescriptorPlan`
- `PipelinePlan`
- `ExecutionShellPlan`
- `AppRegistrationPlan`
- generated manifest/inspection models

Representative planned contributor seams:

- descriptor contributors
- pipeline contributors
- host contributors
- app-registration contributors
- debug/manifest contributors

The migration plans are the authority for the exact future-state shape. This document should describe both the current implementation and the agreed target direction so future work does not drift between the two.

## Architectural guardrails

- Prefer new compile-time metadata over new runtime discovery.
- Keep related generator changes aligned across descriptor, app-registration, and pipeline layers.
- Keep resource-generation changes aligned with the rest of the compile-time model; `ApiResourceSourceGenerator` is part of the same design direction even though it is not module-based.
- Prefer modules, diagnostics, and generated code over hidden runtime conventions.
- If a new feature makes the runtime smarter, ask whether the generator should have made the runtime simpler instead.

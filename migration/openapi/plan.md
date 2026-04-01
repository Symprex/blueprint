# OpenAPI

- Status: Planned
- Priority: P2
- Classification: Core HTTP capability
- Depends on: `../operation-discovery-and-descriptors/plan.md`, `../result-model-and-host-translation/plan.md`, `../http-host-and-message-population/plan.md`, `../resource-model-links-and-post-processing/plan.md`

## Why this module exists

OpenAPI is one of the clearest places where the new source-generated architecture should outperform the legacy runtime/reflection-based model. The old library built the document from runtime descriptors. The rewrite should build as much of the schema and operation metadata as possible from generated descriptors and serializer metadata.

## Evidence

- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint.OpenApi\BlueprintApiBuilderExtensions.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint.OpenApi\OpenApiDocumentBuilder.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint.OpenApi\BlueprintJsonSchemaGenerator.cs`
- Generated usage evidence: `GetOpenApiQuery` appears as a normal generated operation in legacy apps

## Migration goal

Generate OpenAPI documents from the same compile-time metadata that drives endpoint generation and serializer configuration.

## Runtime support to build

- an OpenAPI document builder driven by generated descriptors
- response/outcome metadata driven by flattened descriptor outcomes rather than by raw `Results<T1, ...>` wrappers
- response/body schema metadata that avoids reflection where possible
- explicit integration with `System.Text.Json` source-generation metadata
- optional runtime post-configuration hooks for titles, tags, and schema processors

## Consumer configuration and activation

HTTP hosts should always have OpenAPI support. The long-term model is:

- no `AddOpenApi()` activation requirement
- optional `ConfigureOpenApi(...)`-style customization for details that cannot be inferred statically
- the actual document is backed by compile-time metadata, not runtime scanning of operation types

Current migration-tooling status:

- `BPMIG001` now flags parameterless `BlueprintApiBuilder.AddOpenApi()` calls as redundant safe-deletion cleanup.
- `BPMIG100` now flags configured `AddOpenApi(Action<OpenApiOptions>)` usage as configuration-required migration work that should move toward the future `ConfigureOpenApi(...)` seam instead of being auto-fixed away.

## Integration strategy

The legacy `GetOpenApiQuery` pattern should not define the new architecture by default.

Planned direction:

- OpenAPI is exposed as part of the HTTP host rather than requiring a normal Blueprint operation just to retrieve the document
- if compatibility requires a query-shaped endpoint during migration, treat it as a thin shim over the HTTP host's generated OpenAPI surface rather than as the core model
- operation grouping metadata such as `ApiOperations` should inform tags and grouping in the generated document
- OpenAPI should consume flattened per-operation outcomes from descriptors, not reason about `Results<T1, ...>` wrappers directly
- HTTP-visible unions should default to one response entry per distinct status code; same-status unions require explicit discriminator metadata
- body-bearing resource-event unions are the main generated exception: when every member of a same-status group exposes the generated serialized `eventId`, emit a same-status `oneOf` with discriminator metadata rather than forcing manual configuration
- cross-cutting outcomes from auth/validation/error modules must merge with operation-specific outcomes in the final OpenAPI contract

## Compile-time checks to add

- missing response metadata needed for a valid operation contract
- missing declared failure/error metadata needed for a complete HTTP document
- body/response types not covered by serializer context or schema generation strategy
- unsupported schema shapes that should fail fast instead of producing incomplete OpenAPI
- HTTP operations that cannot produce a valid OpenAPI contract from the available metadata
- same-status union outcomes without an explicit discriminator strategy
- same-status resource-event groups that include a key-only/no-body outcome or otherwise lack a generated serialized `eventId` discriminator
- descriptor outcomes that are incomplete for generated client-safe response contracts

## Observability and debuggability

- generated operation/schema manifest
- snapshot-friendly OpenAPI output for tests
- diagnostics that point back to the operation/response type causing a schema issue
- explicit indication when schema/customisation behavior has been influenced by sensitivity or documentation policies

## Explicit decisions

- Treat OpenAPI as a required part of the HTTP host, not as an optional add-on.
- Lean on serializer metadata rather than separate ad-hoc reflection whenever possible.
- Use OpenAPI generation as a forcing function for descriptor completeness.
- Treat audit sensitivity markers such as `[Sensitive]` as audit/logging concerns by default; any OpenAPI hiding/redaction behavior must be an explicit documentation policy, not an accidental side effect.
- Treat flattened outcomes as the canonical OpenAPI input shape.
- Require distinct HTTP status codes by default for union outcomes; only explicit discriminator metadata allows same-status `oneOf` contracts.
- Allow body-bearing resource-event unions to satisfy that same-status requirement through generated `eventId` discriminator metadata rather than manual per-operation wiring.

## Sequencing

Implement after HTTP descriptors, error-model metadata, and resource metadata are stable enough to describe accurately. Even though the work can land after the base HTTP host, it is still part of HTTP completeness rather than an optional extra.

## Autonomous slice log

- Completed `openapi/project-descriptor-failure-outcomes-into-http-response-metadata`.
- Why it was the right entry slice:
  - the completed error-model work already left stable `OperationFailureOutcomesFeatureData` for both built-in and package-provided translators
  - this workstream already required cross-cutting error outcomes to merge with operation-specific response metadata
- What landed in this slice:
  - `HttpResponseMetadataModule` now projects additive `OperationHttpResponseMetadataFeatureData` for HTTP-exposed operations
  - the projected response entries merge flattened semantic success outcomes with descriptor-advertised failure families
  - each response entry currently carries the status code, whether the response has a body, and the contributing semantic result type names
- Proof closed for this slice:
  - descriptor-source proof for generated `OperationHttpResponseMetadataFeatureData`
  - runtime `Configure().GetFeatureData<OperationHttpResponseMetadataFeatureData>()` proof covering `200`, `422`, and `500`
- Deferred by design:
  - payload/body type metadata for body-bearing responses
  - serializer/schema projection
  - richer same-status discriminator shaping and full OpenAPI document generation
- Autonomous next slice:
  - Continue with `openapi/project-response-body-type-metadata-into-http-response-metadata`.
  - That is the narrowest safe follow-on because the status/body/result-family layer now exists, and the next schema-facing gap is surfacing the body payload contract for body-bearing responses without yet widening to full schema emission or document generation.
- Completed `openapi/project-response-body-type-metadata-into-http-response-metadata`.
- Why it was the right follow-on:
  - the previous response-metadata slice already exposed stable status/body/result-family facts for HTTP-visible outcomes
  - later schema/document slices still lacked the payload contract for those body-bearing responses and would otherwise fall back to executor knowledge or runtime reflection
- What landed in this slice:
  - `OperationHttpResponseMetadata` entries now also carry `BodyTypeNames`
  - `HttpResponseMetadataModule` now projects compile-time-known payload types for typed semantic HTTP results such as `OkResult<T>`, `CreatedResult<T>`, `AcceptedResult<T>`, `ConflictResult<T>`, and `NotFoundResult<T>`
  - the same seam now also records the current host-owned MVC problem-details body contracts for descriptor-advertised validation (`422`) and unhandled (`500`) fallback responses
- Proof closed for this slice:
  - descriptor-source proof for generated HTTP response body-type metadata
  - runtime `Configure().GetFeatureData<OperationHttpResponseMetadataFeatureData>()` proof covering `200`, `422`, and `500` body-type metadata
- Deferred by design:
  - serializer-context coverage and schema projection
  - richer same-status discriminator shaping and full OpenAPI document generation
- Autonomous next slice:
  - Continue with `openapi/project-serializer-context-coverage-feature-data-from-http-response-metadata`.
  - Inspection after the body-type slice showed that there is still no existing `JsonSerializerContext` declaration/config seam in code and the HTTP runtime still serializes through `Newtonsoft.Json`, so the narrowest safe follow-on is a parallel descriptor feature-data inventory derived from `OperationHttpResponseMetadata.BodyTypeNames`, not another direct expansion of the response record and not a runtime host-options change.
- Completed `openapi/project-serializer-context-coverage-feature-data-from-http-response-metadata`.
- Why it was the right follow-on:
  - the response-metadata seam already exposed body payload CLR names per operation
  - there is still no explicit `JsonSerializerContext` declaration/config seam in source and the current HTTP runtime still serializes through `Newtonsoft.Json`, so the safest next step was to make serializer requirements inspectable without changing runtime ownership
- What landed in this slice:
  - `SerializerContextCoverageModule` now derives additive `OperationSerializerContextCoverageFeatureData` from `OperationHttpResponseMetadata.BodyTypeNames`
  - the new feature data records the distinct body payload CLR type names later serializer-context validation and schema slices must account for
  - the slice deliberately leaves runtime JSON execution, app-owned serializer declaration, and diagnostic severity decisions deferred
- Proof closed for this slice:
  - descriptor-source proof for generated `OperationSerializerContextCoverageFeatureData`
  - runtime `Configure().GetFeatureData<OperationSerializerContextCoverageFeatureData>()` proof covering the current `200`, `422`, and `500` body payload types
- Deferred by design:
  - app-owned `JsonSerializerContext` declaration/config
  - coverage validation diagnostics and schema emission
  - runtime serializer switching away from `Newtonsoft.Json`
- Autonomous next slice:
  - Continue with `openapi/declare-app-owned-serializer-context-seam-for-coverage-validation`.
  - That is the narrowest safe follow-on because serializer requirements are now inspectable from descriptor facts, but later validation and schema slices still need an explicit app-owned declaration seam to say which serializer context is expected for a given Blueprint app before any coverage diagnostics can become trustworthy.
- Completed `openapi/declare-app-owned-serializer-context-seam-for-coverage-validation`.
- Why it was the right follow-on:
  - serializer requirements were already inspectable from generated descriptor facts, but no app-owned contract said which serializer context was authoritative for a Blueprint app
  - later coverage diagnostics needed that consumer-declared seam before they could distinguish missing serializer coverage from missing configuration
- What landed in this slice:
  - new `[BlueprintAppSerializerContext(JsonSerializerContextType = typeof(...))]` contract in `Blueprint.Runtime.Model`
  - `BlueprintApp.AppOwnedSerializerContextType` plus app-generator parsing/emission so generated app constructors persist the declared serializer-context type
  - the integration test app and `Blueprint.SourceGenerator.Sample` now both declare serializer contexts through the new seam
- Proof closed for this slice:
  - app-generator snapshot proof for emitted serializer-context assignment
  - runtime app-model integration proof for constructed app `AppOwnedSerializerContextType`
  - full verification sweep: `dotnet build .\Blueprint.sln --no-restore` and the three repository test commands passed after the sample/docs updates
- Deferred by design:
  - compile-time diagnostics validating body-type coverage against the declared serializer context
  - runtime serializer switching away from `Newtonsoft.Json`
  - full schema projection and document emission
- Autonomous next slice:
  - Continue with `openapi/validate-serializer-context-coverage-against-app-declaration`.
  - That is the narrowest safe follow-on because body payload types are already inventoried on descriptors and the app now declares the expected `JsonSerializerContext`, so the next missing seam is compile-time coverage validation rather than more ownership work or runtime serializer changes.
- Completed `openapi/validate-serializer-context-coverage-against-app-declaration`.
- Why it was the right follow-on:
  - body payload requirements were already inspectable from descriptor facts and the app-owned declaration seam was now explicit, so the next missing contract was deterministic compile-time validation instead of more ownership plumbing
  - the prior slice explicitly deferred distinguishing incomplete declared coverage from missing configuration, and this slice closes the first half of that split
- What landed in this slice:
  - `AppDataModelSourceGenerator` now reuses `DescriptorPlanBuilder` before registration emission so app-level serializer validation shares descriptor contributor ordering, host metadata, and exception-policy construction
  - `BP006` now reports one app-level diagnostic when the declared serializer context omits descriptor-derived HTTP response body payload types required by the scanned operations
  - the integration test app serializer context now includes `ProblemDetails` and `ValidationProblemDetails`, matching the current descriptor-projected HTTP fallback body contracts
  - synthetic build telemetry snapshots now record the new `ExecutionPlan` and `ExceptionTranslationPolicy` tracked stages for `AppDataModelSourceGenerator`
- Proof closed for this slice:
  - app-generator diagnostic snapshot proof for missing serializer coverage
  - app-generator success snapshot proof for satisfied serializer coverage
  - repo verification sweep: `dotnet build .\Blueprint.sln --no-restore`, `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj`, `dotnet test .\src\Blueprint.SourceGenerator.CodeWriter.Tests\Blueprint.SourceGenerator.CodeWriter.Tests.csproj`, and `dotnet test .\src\Blueprint.SourceGenerator.Tests.Integration\Blueprint.SourceGenerator.Tests.Integration.csproj`
- Deferred by design:
  - diagnosing HTTP coverage requirements when no app-owned serializer context is declared at all
  - runtime serializer switching away from `Newtonsoft.Json`
  - full schema projection and document emission
- Autonomous next slice:
  - Continue with `openapi/diagnose-missing-app-serializer-context-declaration-for-http-coverage`.
  - That is the narrowest safe follow-on because the current validation now covers the "declared but incomplete" half of the contract, while the remaining missing-configuration diagnostic is still needed before broader schema/document work.
- Completed `openapi/diagnose-missing-app-serializer-context-declaration-for-http-coverage`.
- Why it was the right follow-on:
  - the prior serializer-context coverage slice deliberately stopped at "declared but incomplete" validation, so HTTP-visible apps still had no deterministic compile-time failure when coverage was required but no serializer context was declared
  - schema/document work still needs that missing-configuration guardrail before it can trust the app-owned serializer contract as the source of truth for OpenAPI projection
- What landed in this slice:
  - `AppDataModelSourceGenerator` now reports `BP007` when descriptor-derived HTTP body coverage requirements exist but the app declares no `[BlueprintAppSerializerContext(JsonSerializerContextType = typeof(...))]`
  - the missing-declaration diagnostic is anchored to the app type declaration through the parsed `DeclarationLocation`, while `BP006` remains anchored to the serializer-context declaration for incomplete declared coverage
  - the shared synthetic scale fixture now emits an app-owned serializer context for link-heavy HTTP-visible shapes so the verification harness continues to model the current app-owned contract accurately
- Proof closed for this slice:
  - app-generator diagnostic proof for missing serializer-context declaration plus a green-path proof for apps with no HTTP serializer coverage requirements
  - focused tracked scale-fixture regression proof covering descriptor + app generator interaction for link-heavy fixtures
  - fresh repo verification sweep: `dotnet format .\Blueprint.sln --no-restore`, `dotnet build .\Blueprint.sln --no-restore`, `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --no-restore`, `dotnet test .\src\Blueprint.SourceGenerator.CodeWriter.Tests\Blueprint.SourceGenerator.CodeWriter.Tests.csproj --no-restore`, and `dotnet test .\src\Blueprint.SourceGenerator.Tests.Integration\Blueprint.SourceGenerator.Tests.Integration.csproj --no-restore`
- Deferred by design:
  - projecting declared serializer-context coverage into schema-ready OpenAPI metadata
  - runtime serializer switching away from `Newtonsoft.Json`
  - full schema projection and document emission
- Autonomous next slice:
  - Continue with `openapi/project-schema-coverage-from-serializer-context-declaration`.
  - That is the narrowest safe follow-on because the codebase now knows both sides of the serializer contract: descriptor-derived required body types and the app-declared serializer context. The next schema-facing gap is projecting the declared context into stable coverage facts OpenAPI can consume, without yet widening into full document emission or runtime serializer changes.
- Completed `openapi/project-schema-coverage-from-serializer-context-declaration`.
- Why it was the right follow-on:
  - the previous OpenAPI slices had already established the required side of the serializer contract on descriptors and the app-owned declaration/validation seam, but later schema/document work still lacked a durable app-level inventory of which `JsonSerializable(...)` payloads the app had actually declared
  - projecting that declared inventory onto the generated app model was the narrowest way to make serializer coverage consumable by later OpenAPI work without leaking app-specific facts into app-agnostic descriptor feature data
- What landed in this slice:
  - `BlueprintApp` now exposes `AppOwnedSerializerContextBodyTypeNames` as the app-owned declared body payload CLR type inventory for later OpenAPI/schema consumers
  - `AppDataModelSourceGenerator` now parses distinct `JsonSerializable(...)` body type names from the declared serializer context once and persists them through the generated app registration constructor beside `AppOwnedSerializerContextType`
  - app-level serializer coverage validation (`BP006`/`BP007`) now reuses that parsed declared inventory instead of re-inspecting the serializer context symbol in a second later stage
- Proof closed for this slice:
  - app-generator source proof for emitted `AppOwnedSerializerContextBodyTypeNames` assignment when the declared serializer context includes `JsonSerializable(...)` entries
  - runtime app-model integration proof for constructed app `AppOwnedSerializerContextBodyTypeNames`
  - fresh repo verification sweep: `dotnet format .\Blueprint.sln --no-restore`, `dotnet build .\Blueprint.sln --no-restore`, `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --no-restore`, `dotnet test .\src\Blueprint.SourceGenerator.CodeWriter.Tests\Blueprint.SourceGenerator.CodeWriter.Tests.csproj --no-restore`, and `dotnet test .\src\Blueprint.SourceGenerator.Tests.Integration\Blueprint.SourceGenerator.Tests.Integration.csproj --no-restore`
- Deferred by design:
  - establishing the first OpenAPI document-builder/runtime seam that consumes both descriptor-published serializer requirements and the app-owned declared serializer inventory
  - full schema projection and document emission
  - runtime serializer switching away from `Newtonsoft.Json`
- Autonomous next slice:
  - Continue with `openapi/establish-openapi-document-builder-seam-consuming-app-serializer-context`.
  - That is the narrowest safe follow-on because descriptor feature data remains app-agnostic, so the next missing OpenAPI seam must live at the app/runtime or document-builder boundary where `BlueprintApp.AppOwnedSerializerContextBodyTypeNames` can be combined with per-operation descriptor coverage facts without widening yet into full schema rendering or document emission.
- Completed `openapi/establish-openapi-document-builder-seam-consuming-app-serializer-context`.
- Why it was the right follow-on:
  - the repository now had both serializer inputs needed for OpenAPI work, but there was still no single runtime seam that combined the app-owned declared serializer inventory with per-operation HTTP response and serializer-coverage metadata
  - later schema/document slices would otherwise have to rediscover those facts ad hoc from `BlueprintApp` plus descriptor feature data instead of consuming one stable HTTP/OpenAPI-specific context
- What landed in this slice:
  - new `Blueprint.Runtime.Http.OpenApiDocumentBuilderContext.Create(app)` seam that snapshots the constructed app, its declared serializer-context type, its declared body payload CLR names, and the HTTP-exposed operations prepared for later OpenAPI work
  - per-operation `OpenApiDocumentOperationContext` entries that carry `OperationHttpResponseMetadataFeatureData`, `OperationSerializerContextCoverageFeatureData`, the missing declared serializer body types, and a deterministic coverage-satisfied flag
  - the seam stays deliberately host-specific and runtime-facing: it only inspects `app.GetOperationsForHost("http")` and does not widen into schema generation, document emission, or `BlueprintHttpOptions` ownership
- Proof closed for this slice:
  - failing-then-passing runtime integration proof for a covered app-owned serializer declaration using `IntegrationTestBlueprintApp`
  - failing-then-passing runtime integration proof for an uncovered app shell that registers the same HTTP descriptor but declares no serializer context
  - the proof also asserts the seam excludes non-HTTP operations from the OpenAPI snapshot
- Deferred by design:
  - JSON schema projection for the covered serializer-context body types
  - full OpenAPI document emission and HTTP endpoint wiring
  - runtime serializer switching away from `Newtonsoft.Json`
- Autonomous next slice:
  - Continue with `openapi/establish-schema-projection-seam-for-covered-body-types`.
  - That is the narrowest safe follow-on because the new document-builder seam now centralizes the declared serializer inventory plus per-operation required body metadata, so the next OpenAPI gap is projecting covered body payload types into a stable schema-ready shape without yet widening into full operation/document rendering.
- Completed `openapi/establish-schema-projection-seam-for-covered-body-types`.
- Why it was the right follow-on:
  - the document-builder seam had already centralized the app-owned serializer declaration and the per-operation coverage requirements, but later OpenAPI work still lacked a durable schema-ready model derived from actual serializer metadata
  - app registrations previously persisted only serializer type/name facts, which would have forced later runtime schema work to rediscover typed serializer coverage or reflect over serializer metadata ad hoc
- What landed in this slice:
  - `BlueprintApp` now also persists `AppOwnedSerializerContext` and `AppOwnedSerializerContextBodyTypes` beside the existing serializer declaration facts
  - `AppDataModelSourceGenerator` now emits the constructed serializer context instance plus the typed declared `JsonSerializable(...)` inventory through generated app registrations
  - new `Blueprint.Runtime.Http.OpenApiSchemaProjectionContext.Create(document)` seam projects covered serializer-backed body types into ordered `OpenApiSchemaModel` entries with broad `OpenApiSchemaKind` values and ordered property-name inventories
- Proof closed for this slice:
  - generator source proof for emitted `AppOwnedSerializerContext` and `AppOwnedSerializerContextBodyTypes` assignments
  - runtime app-model integration proof for the constructed serializer-context instance plus typed body inventory
  - runtime integration proof for `OpenApiSchemaProjectionContext` covering both the covered-app path and the uncovered-app exclusion path
  - fresh repo verification sweep: `dotnet format .\Blueprint.sln --no-restore`, `dotnet build .\Blueprint.sln --no-restore`, `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --no-restore`, `dotnet test .\src\Blueprint.SourceGenerator.CodeWriter.Tests\Blueprint.SourceGenerator.CodeWriter.Tests.csproj --no-restore`, and `dotnet test .\src\Blueprint.SourceGenerator.Tests.Integration\Blueprint.SourceGenerator.Tests.Integration.csproj --no-restore`
- Deferred by design:
  - per-response schema reference projection
  - full OpenAPI document emission and HTTP endpoint wiring
  - runtime serializer switching away from `Newtonsoft.Json`
- Autonomous next slice:
  - Continue with `openapi/project-response-schema-references-from-covered-body-types`.
  - That is the narrowest safe follow-on because the runtime now has stable type-level schema models for covered serializer-backed body payloads, but per-operation HTTP response metadata still only carries body-type names. The next OpenAPI gap is merging response metadata with covered schema projections into per-response schema references before widening into full document rendering or endpoint wiring.
- Completed `openapi/project-response-schema-references-from-covered-body-types`.
- Why it was the right follow-on:
  - the previous schema projection slice established stable covered `OpenApiSchemaModel` entries, but per-response HTTP metadata still only carried body-type names
  - later OpenAPI document work would otherwise have to recompute response-to-schema joins ad hoc instead of consuming one deterministic runtime seam
- What landed in this slice:
  - new `Blueprint.Runtime.Http.OpenApiResponseSchemaReferenceProjectionContext.Create(schemaProjection)` seam that preserves each `OperationHttpResponseMetadata` response entry while resolving its covered `BodyTypeNames` onto `OpenApiResponseSchemaReference` records
  - per-operation `OpenApiResponseSchemaReferenceOperationContext` entries that keep the existing response ordering and metadata while layering in the resolved schema references needed for later document emission
  - the seam stays deliberately host-specific and runtime-facing: it composes the existing document-builder and schema-projection seams without widening into OpenAPI document rendering, endpoint wiring, or `BlueprintHttpOptions` ownership
- Proof closed for this slice:
  - failing-then-passing runtime integration proof for a covered generated app using `IntegrationTestBlueprintApp`
  - failing-then-passing runtime integration proof for an uncovered manual app shell that registers the same HTTP descriptor but exposes no serializer context
  - the proof also asserts the seam preserves the current `200`, `422`, and `500` response ordering while only adding schema references for covered body types
- Deferred by design:
  - property-level schema type and constraint metadata from serializer metadata
  - full OpenAPI document emission and HTTP endpoint wiring
  - runtime serializer switching away from `Newtonsoft.Json`
- Autonomous next slice:
  - Continue with `openapi/project-property-types-and-constraints-from-serializer-metadata-into-schema-models`.
  - That is the narrowest safe follow-on because response metadata now links deterministically to covered schema models, but the current schema contract still only exposes body names, broad kinds, and ordered property-name inventories. The next OpenAPI gap is enriching those schema models with property-level type and constraint metadata from `System.Text.Json` before widening into document rendering or endpoint wiring.
- Completed `openapi/project-property-types-and-constraints-from-serializer-metadata-into-schema-models`.
- Why it was the right follow-on:
  - the response/schema-reference slice established stable links from HTTP response families onto covered schema models, but those schema models still only exposed body names, broad kinds, and ordered property-name inventories
  - later OpenAPI schema/document work needed richer property-level shape facts from `System.Text.Json` metadata without reopening descriptor generation, host wiring, or serializer-coverage analysis
- What landed in this slice:
  - `Blueprint.Runtime.Http.OpenApiSchemaProjectionContext.Create(document)` now projects ordered `OpenApiSchemaPropertyModel` entries on each `OpenApiSchemaModel`, while preserving the existing `PropertyNames` compatibility surface for already-landed runtime seams
  - the projected property metadata captures the serializer-owned facts currently available from `System.Text.Json`: broad property kind, `IsRequired`, and `IsExtensionData`
  - scalar property kinds now map deterministically for string-like, boolean, integer, and number CLR shapes while object/array/dictionary kinds continue to come from serializer metadata
- Proof closed for this slice:
  - failing-then-passing runtime integration proof using an isolated manual app/descriptor fixture with a custom response DTO that mixes required string, integer, boolean, nested-object, array, dictionary, and extension-data properties
  - nearby OpenAPI integration regression sweep covering the pre-existing schema projection and response-schema reference seams alongside the new property-metadata proof
- Deferred by design:
  - validation/data-annotation constraints not surfaced by `System.Text.Json` metadata
  - reusable OpenAPI schema component rendering
  - full OpenAPI document emission and HTTP endpoint wiring
  - runtime serializer switching away from `Newtonsoft.Json`
- Autonomous next slice:
  - Continue with `openapi/render-openapi-schema-components-from-projected-schema-models`.
  - That is the narrowest safe follow-on because the runtime now has stable per-schema property metadata, but no seam yet renders those models into reusable OpenAPI schema components. Rendering components in isolation keeps schema-shape proof separate from response/operation/document assembly and is narrower than full document rendering.
- Completed `openapi/render-openapi-schema-components-from-projected-schema-models`.
- Why it was the right follow-on:
  - the prior schema-projection slice already established stable per-schema property metadata from serializer-owned facts, but there was still no reusable OpenAPI-facing contract that rendered those schema models into component-shaped output
  - later operation/document slices need a deterministic schema-component layer before they can bind response metadata or operation structure onto those schemas without recomputing shape rules
- What landed in this slice:
  - `Blueprint.Runtime.Http.OpenApiSchemaComponentRenderingContext.Create(schemaProjection)` now renders the projected covered-schema inventory into ordered `OpenApiSchemaComponentModel` entries with OpenAPI-style `Type`, ordered `RequiredPropertyNames`, and ordered `OpenApiSchemaComponentPropertyModel` members
  - component property rendering currently carries the serializer-owned facts already exposed by `OpenApiSchemaPropertyModel`: broad property type plus `IsExtensionData`, while requiredness remains modeled at the component level through `RequiredPropertyNames`
  - the seam deliberately consumes `OpenApiSchemaProjectionContext` directly so schema rendering stays isolated from response grouping and document assembly
- Proof closed for this slice:
  - failing-then-passing runtime integration proof covering rendered components for covered app-owned serializer types, uncovered apps with no rendered components, and richer property-metadata rendering from the isolated serializer-backed DTO fixture
  - nearby OpenAPI integration regression sweep covering document-builder, schema-projection, property-metadata, response-schema-reference, and schema-component seams together
- Deferred by design:
  - per-response OpenAPI response-object rendering
  - operation-level OpenAPI rendering and full document emission
  - validation/data-annotation constraints not surfaced by `System.Text.Json` metadata
  - runtime serializer switching away from `Newtonsoft.Json`
- Autonomous next slice:
  - Continue with `openapi/render-openapi-response-objects-from-schema-components`.
  - That is the narrowest safe follow-on because reusable schema components now exist in isolation, but the current response metadata still does not render stable OpenAPI response-object shapes that bind status codes and response-body metadata onto those components. Rendering response objects keeps response-level proof separate from operation/document assembly and is narrower than jumping straight to full document rendering.
- Completed `openapi/render-openapi-response-objects-from-schema-components`.
- Why it was the right follow-on:
  - reusable schema components already existed in isolation, but the runtime still had no OpenAPI-facing response-object seam that reconnected status/body/result metadata to those rendered components
  - rendering response objects remained narrower and easier to verify than widening directly into operation or full document assembly
- What landed in this slice:
  - new `Blueprint.Runtime.Http.OpenApiResponseObjectRenderingContext.Create(responseSchemaProjection)` seam that composes the response-schema-reference and schema-component seams without widening into operation/document assembly
  - per-operation `OpenApiResponseObjectOperationContext` entries that preserve response ordering while rendering `OpenApiResponseObjectModel` instances with status code, body-presence flag, semantic result names, and deterministic JSON content entries for covered schema components
  - uncovered apps or uncovered body types now keep the underlying response metadata but render no response content, preserving the existing coverage boundary instead of inventing runtime fallback schemas
- Proof closed for this slice:
  - failing-then-passing runtime integration proof for a covered generated app using `IntegrationTestBlueprintApp`
  - failing-then-passing runtime integration proof for an uncovered manual app shell that registers the same HTTP descriptor but exposes no serializer context
- Deferred by design:
  - operation-level OpenAPI rendering that binds operation identity, URLs, and response objects together
  - full OpenAPI document emission and HTTP endpoint wiring
  - validation/data-annotation constraints not surfaced by `System.Text.Json` metadata
  - runtime serializer switching away from `Newtonsoft.Json`
- Autonomous next slice:
  - Continue with `openapi/render-openapi-operations-from-response-objects`.
  - That is the narrowest safe follow-on because response objects now exist as stable runtime contracts, but the runtime still lacks an operation-level OpenAPI seam that binds operation identity and URLs onto those rendered responses before widening into full document assembly.
- Completed `openapi/render-openapi-operations-from-response-objects`.
- Why it was the right follow-on:
  - response objects were already stable runtime contracts, but the OpenAPI chain still had no seam that bound descriptor-published operation identity and route formats onto those responses
  - rendering operation models remained narrower and easier to verify than jumping straight to path grouping or full document emission
- What landed in this slice:
  - new `Blueprint.Runtime.Http.OpenApiOperationRenderingContext.Create(responseObjects)` seam that composes the response-object layer with operation identity and deterministic route-format ordering
  - per-operation `OpenApiOperationModel` entries now carry the descriptor-published `OperationName`, stable CLR `OperationTypeName`, ordinal-sorted `UrlFormats`, and the previously rendered response objects without widening into path-item or document assembly
  - manual integration fixtures now prove the seam preserves empty URL sets and sorts multiple URL formats deterministically instead of depending on descriptor registration order
- Proof closed for this slice:
  - failing-then-passing runtime integration proof for a covered generated app using `IntegrationTestBlueprintApp`
  - failing-then-passing runtime integration proofs for manual HTTP app shells covering deterministic multi-URL ordering and zero-URL preservation
- Deferred by design:
  - projecting HTTP method metadata for OpenAPI-visible operations
  - path-item/path grouping and full OpenAPI document emission
  - validation/data-annotation constraints not surfaced by `System.Text.Json` metadata
  - runtime serializer switching away from `Newtonsoft.Json`
- Autonomous next slice:
  - Continue with `openapi/project-operation-http-method-metadata-from-descriptor-attributes`.
  - That is the narrowest safe follow-on because operation rendering now binds identity and URL formats onto response objects, but path-item/document assembly still needs stable HTTP method metadata and the current repo only exposes dormant `HttpMethodAttribute` contracts plus commented legacy runtime hooks instead of a live descriptor/runtime seam.

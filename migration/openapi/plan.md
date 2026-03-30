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

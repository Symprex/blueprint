# OpenAPI

- Status: Planned
- Priority: P2
- Classification: Core HTTP capability
- Depends on: `../operation-discovery-and-descriptors/plan.md`, `../http-host-and-message-population/plan.md`, `../resource-model-links-and-post-processing/plan.md`

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
- response/body schema metadata that avoids reflection where possible
- explicit integration with `System.Text.Json` source-generation metadata
- optional runtime post-configuration hooks for titles, tags, and schema processors

## Consumer configuration and activation

HTTP hosts should always have OpenAPI support. The long-term model is:

- no `AddOpenApi()` activation requirement
- optional `ConfigureOpenApi(...)`-style customization for details that cannot be inferred statically
- the actual document is backed by compile-time metadata, not runtime scanning of operation types

## Integration strategy

The legacy `GetOpenApiQuery` pattern should not define the new architecture by default.

Planned direction:

- OpenAPI is exposed as part of the HTTP host rather than requiring a normal Blueprint operation just to retrieve the document
- if compatibility requires a query-shaped endpoint during migration, treat it as a thin shim over the HTTP host's generated OpenAPI surface rather than as the core model
- operation grouping metadata such as `ApiOperations` should inform tags and grouping in the generated document

## Compile-time checks to add

- missing response metadata needed for a valid operation contract
- missing declared failure/error metadata needed for a complete HTTP document
- body/response types not covered by serializer context or schema generation strategy
- unsupported schema shapes that should fail fast instead of producing incomplete OpenAPI
- HTTP operations that cannot produce a valid OpenAPI contract from the available metadata

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

## Sequencing

Implement after HTTP descriptors, error-model metadata, and resource metadata are stable enough to describe accurately. Even though the work can land after the base HTTP host, it is still part of HTTP completeness rather than an optional extra.

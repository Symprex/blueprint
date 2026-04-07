# HTTP host and message population

- Status: In Progress — Slice 1 complete
- Priority: P1
- Classification: Core host
- Depends on: `../foundation-module-system/plan.md`, `../operation-discovery-and-descriptors/plan.md`, `../pipeline-composition-and-execution/plan.md`, `../result-model-and-host-translation/plan.md`

## Why this module exists

HTTP is the primary host and the main consumer-facing contract surface. The legacy library combined `HttpHost`, message-population sources, result executors, formatters, and route metadata. The source-generated rewrite must preserve those semantics while moving route/binding decisions to compile time.

## Evidence

- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint.Http\HttpHost.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint.Http\BlueprintApiBuilderExtensions.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\Middleware\MessagePopulationMiddlewareBuilder.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint.Http\MessagePopulation\HttpBodyMessagePopulationSource.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint.Http\MessagePopulation\HttpRouteMessagePopulationSource.cs`
- Legacy generated output: `D:\Projects\Signature365\src\Apps\AdminPortal\Symprex.Signature365.AdminPortal\Internal\Generated\Blueprint\GetOpenApiQueryExecutorPipeline.cs`
- Current: `src\Blueprint.Runtime\Http\ApplicationBuilderExtensions.cs`
- Current: `src\Blueprint.SourceGenerator\Modules\LinkGeneration\LinkGenerationModule.cs`

## Migration goal

Produce a source-generated HTTP host that knows, at compile time:

- which operations are HTTP-visible
- which route and method each operation uses
- where each property comes from (route, body, query, header, cookie, form, ambient context)
- which result executor/output formatter path applies
- which serializer context is required
- which host-agnostic result shapes and HTTP hints it must execute

## Runtime support to build

- Endpoint mapping for generated apps
- Generated message-population plan per operation
- Generated result-execution plan per operation result type
- Host executors/adapters that translate semantic result types plus attached HTTP hints into status code, headers, and body behavior
- Minimal runtime helpers for body parsing, formatter selection, HTTP context access, and host-hint application
- Strong `System.Text.Json` source-generation alignment for request/response bodies
- Output formatter and content-negotiation support that can still choose among enabled formatters at runtime without giving up compile-time knowledge of supported result shapes

## Consumer configuration and activation

Preserve or replace with thin source-generated equivalents:

- `.Http(...)`
- `.AddResourceEvents<T>()`
- `[Link]`
- `[HttpMethod(...)]`
- `[FromBody]`, `[FromQuery]`, `[FromHeader]`, `[FromCookie]`

The consumer should still be able to express the API contract declaratively; the generator should turn that into a deterministic binding plan.

`AddHateoasLinks()` should not remain the long-term activation switch. Link generation should be architecturally available whenever resource/link metadata exists and should compile away when it does not.

## Compile-time checks to add

- route placeholders map to real operation properties
- binding source conflicts for the same property
- unsupported body-binding shapes
- missing serializer context coverage for HTTP payloads
- result type has no known HTTP execution strategy
- unsupported or conflicting attached HTTP hints for the declared result shape
- operation marked for HTTP without route metadata
- GET/POST semantics that conflict with configured binding behavior
- no formatter can satisfy the declared HTTP response contract

## Observability and debuggability

Generate or expose:

- route table manifest
- per-operation binding plan
- serializer context dependencies
- result-execution mapping
- the OpenAPI contract view that will be emitted for the same operations
- enabled formatters and content-negotiation behavior
- developer-friendly diagnostics for binding failures and unsupported shapes

## Explicit decisions

- Prefer compile-time binding plans over runtime source iteration.
- Keep host visibility as compile-time metadata.
- Push JSON metadata and schema friendliness toward `System.Text.Json` source generation.
- Treat HTTP as implying OpenAPI generation and publication. `ConfigureOpenApi` may remain, but `AddOpenApi` should not be required to make the HTTP host complete.
- Treat HTTP as the main path, but not the only host concept in the system.
- Keep core result types host agnostic. The HTTP host owns status-code/header/body translation for those results.
- Support typed success results plus attachable HTTP hints such as `ETag` and cache control without requiring result types to embed HTTP execution behavior.

## Sequencing

Implement early because many other modules depend on HTTP feature data, request context, and result handling behavior.

### Completed slices

- **Slice 1**: Route/body property ownership and Population pipeline stage
- **Slice 2**: Query string binding with camelCase and type conversions
- **Slices 3+4**: [FromX] attribute overrides + header/cookie extraction
- **Slice 5**: Claims identity, base URI, [MustBeAuthenticated] pipeline enforcement
- **Slice 6**: OpenAPI parameter and request body projection from property ownership

### Next slices (improvement pass)

- **Slice 7**: Shared feature data constants — `WellKnownFeatureData` static class to eliminate per-descriptor allocation of identical ImmutableArrays (failure outcomes, host exposure, HTTP methods, auth requirement)
- **Slice 8**: Semantic pipeline result types — replace `HttpPipelineShortCircuitResult` + direct status code writes with `UnauthenticatedResult` (401), `BindingFailureResult` (400), `UnsupportedMediaTypeResult` (415)
- **Slice 9**: Shared host executor shell — `OperationExecutorShell` base class owning activity/logging/exception-handling boilerplate, shrinking every generated pipeline by ~50–80 lines

### Future slices

- Content negotiation and output formatting
- OpenAPI projection of pipeline failure outcomes (400/401/415 in spec)

# Decision 001: No General Nested Operation Execution

**Status:** Closed  
**Date:** 2025-07-18  
**Gate:** Phase 1 re-query decision gate (`migration/plan.md:253`)  
**Scope:** Signature365 consumer-contract compatibility — not arbitrary legacy extensibility

## Decision

General nested operation execution (re-query) is **removed** from the source-generated Blueprint stack. The generated pipeline does not support invoking another operation from within a handler's execution pipeline.

Handlers that need to compose behaviour should:

1. Call services directly via normal dependency injection.
2. Return concrete result types that the pipeline understands.

No internal re-query facility is needed.

## Context

Legacy Blueprint's `IApiOperationExecutor.ExecuteAsync` supported nested invocations from within a handler. The only authored-code usage in Signature365 was the resource-event self-query path (`Data == null && SelfQuery != null`) inside `ResourceEventHandler.cs`.

That path served two purposes:

1. Providing `Href` for event payloads — now solved by compile-time link metadata.
2. Providing `Data` for events where the handler does not already have it — now solved by handlers directly constructing typed `ResourceEventResult<T>` results.

## Negative audit evidence

The Signature365 authored-code audit found **no** `CreateNested(...)`, `ExecuteNestedAsync(...)`, or `SelfQuery =` assignments in `src\Apps` or `src\Core` outside generated/framework infrastructure code. The 154 `[FromResource]` and 140 `{Auth:...}` usages are authorisation-layer bindings, not nested execution.

## Replacement contracts

| Legacy pattern | New contract |
|---|---|
| `SelfQuery` for Href | Compile-time link metadata via `[SelfLink]`/`[RootLink]` |
| `SelfQuery` for body Data | Handler constructs `ResourceEventResult<T>` directly |
| Key-only / deleted events | `KeyOnlyResourceEventResult` (identity only, no body) |
| Body-bearing events | `ResourceEventResult<T>` (typed payload, JSON 201 envelope) |
| Same-status discrimination | Compile-time `[ResourceEvent("...")]` metadata → `eventId` discriminator |

## Proven implementation evidence

- `ResourceEventResult<T>` and `KeyOnlyResourceEventResult` exist and are tested: `src\Blueprint.Runtime\Exec\ResourceEventResult.cs`, `src\Blueprint.Runtime\Exec\KeyOnlyResourceEventResult.cs`
- HTTP translation proven: empty 201 for key-only, JSON 201 envelope for body-bearing: `src\Blueprint.Runtime\Http\HttpOperationResultExecutor.cs:84-94`
- Same-status OpenAPI discrimination proven via compile-time event metadata and `eventId` field: `src\Blueprint.SourceGenerator.Tests\Generators\Descriptors\OperationDescriptorSourceGeneratorTests.ResourceEventMetadata.cs`
- Result translation end-to-end tests: `src\Blueprint.SourceGenerator.Tests\Artifacts\OperationResultTranslationEndpointTests.cs:155-187`

## What remains Phase 2 work (not part of this gate)

- Compile-time href/link binding for key-only and partial-body resource-event cases.
- Full resource-event family declaration surface (`[ResourceEventFamily]`, `[ResourceEventReason]`).
- Auditing integration with the save-only audit contract.
- Migration diagnostic for legacy code that attempts nested execution (`BPMIG` reserved prefix).

## Migration guidance

Consumer code that previously relied on nested execution should:

1. Move the data retrieval into the handler itself (call the service/repository directly).
2. Return a concrete result type (`ResourceEventResult<T>` or `KeyOnlyResourceEventResult`).
3. If link Href is needed, it is generated from compile-time metadata — no runtime query required.

Body-bearing handlers should return `ResourceEventResult<T>` (generic form), not raw `ResourceEventResult`, to preserve compile-time schema and discriminator value.

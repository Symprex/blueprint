# Operation discovery and descriptors

- Status: Planned
- Priority: P1
- Classification: Core
- Depends on: `../foundation-module-system/plan.md`

## Why this module exists

The legacy library used `OperationScanner`, `IOperationScannerConvention`, `IBlueprintHost`, and descriptor feature data to decide which operations existed, which hosts could expose them, and what metadata would be available to later middleware and HTTP/OpenAPI processing.

The current rewrite already has strong foundations in `OperationDescriptorSourceGenerator`, `AppDataModelSourceGenerator`, and `ApiResourceSourceGenerator`, but it does not yet recover the richer host/convention/feature-data model.

## Evidence

- Current: `src\Blueprint.SourceGenerator\Generators\Descriptors\OperationDescriptorSourceGenerator.cs`
- Current: `src\Blueprint.SourceGenerator\Generators\App\AppDataModelSourceGenerator.cs`
- Current: `src\Blueprint.SourceGenerator\Generators\ApiResources\ApiResourceSourceGenerator.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\Configuration\IOperationScannerConvention.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\Configuration\IBlueprintHost.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\ApiOperationDescriptor.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint.Http\HttpHost.cs`
- Signature365 extension evidence: `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common.Core\Api\OperationsHandlerScanner.cs`

## Migration goal

Move all operation discovery, host inclusion, and feature-data enrichment to compile time while keeping the consumer-facing command/query/resource contracts familiar.

## Runtime support to build

- Richer generated `ApiOperationDescriptor` instances that carry precomputed feature data.
- Generated app registration that knows which operations belong to which host/app.
- A descriptor model that can support HTTP, background-task, message-host, link, validation, auth, and OpenAPI metadata without runtime scanning.
- Resource metadata generation (`ApiResourceSourceGenerator`) as part of the overall descriptor story, not a disconnected extra.
- Success/response contract metadata that captures what legacy `IReturn<TResponse>` and host conventions relied on, so downstream modules can reason about response bodies, error shapes, and serializer needs.
- Flattened operation outcomes that expand `Results<T1, ...>` into concrete per-outcome descriptor metadata while keeping the raw wrapper as a generator-internal concern.
- Descriptor metadata for same-status outcome groups when a compile-time-safe discriminator exists, especially generated `eventId`-based resource-event groups.

## Consumer configuration and activation

The source-generated rewrite should support:

- app-level scan roots (`[BlueprintAppScan]` or equivalent)
- explicit host/module configuration through registration helpers
- operation-level attributes that continue to define behavior (`[Link]`, auth attributes, validation attributes, task markers, etc.)
- core grouped-handler conventions such as `ApiOperations` / `OperationsHandlerScanner`
- future explicit include/exclude rules where legacy builder code previously used conventions

The legacy fluent builder can remain as a thin configuration API, but discovery itself should stay compile-time driven.

## Compile-time checks to add

- operation type is supported by at least one enabled host
- conflicting host exposure rules
- missing partial modifiers where generation needs partial types
- invalid or conflicting feature data emitted by multiple contributors
- unsupported response-shape inference for downstream modules
- invalid resource metadata generation assumptions
- response-contract inference failures where HTTP/OpenAPI/output-formatting modules need a known success type
- union outcome metadata that cannot be flattened into an unambiguous descriptor shape
- same-status flattened outcome groups that do not have a compile-time-safe discriminator strategy

## Observability and debuggability

Generate or expose:

- a discovered-operations manifest
- host exposure per operation
- descriptor feature summary per operation
- module/convention provenance for important metadata

This should replace the need to infer discovery outcomes from runtime model objects.

## Explicit decisions

- Reintroduce the *concepts* of conventions and hosts, but implement them at compile time.
- Hosts are first-class concepts that use module contributors; they are not just another metadata side effect.
- Keep descriptor feature data as a first-class extensibility mechanism.
- Treat `ApiResourceSourceGenerator` as part of the descriptor/discovery model, not as an isolated special case.
- Treat `ApiOperations` / grouped handler discovery as core Blueprint functionality rather than as a separate package concern.
- Avoid runtime type scanning and reflection-based host filtering.
- Keep `IQuery<T>` / `ICommand<T>` as the consumer-facing contract surface, but let descriptor generation expand `Results<T1, ...>` into flattened outcomes for downstream modules.
- Let descriptor generation carry same-status discriminator metadata forward rather than forcing HTTP/OpenAPI layers to rediscover it.

## Sequencing

Implement after the foundation module system and before any module that depends on host or descriptor feature data.

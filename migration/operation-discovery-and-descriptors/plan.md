# Operation discovery and descriptors

- Status: Active
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

- Richer generated `ApiOperationDescriptor` instances that carry precomputed feature data, including explicit auth/access metadata and provenance.
- Generated app registration that knows which operations belong to which host/app.
- A descriptor model that can support HTTP, background-task, message-host, link, validation, auth, and OpenAPI metadata without runtime scanning.
- Resource metadata generation (`ApiResourceSourceGenerator`) as part of the overall descriptor story, not a disconnected extra.
- `[FromResource(...)]` expansion is a descriptor-enrichment step: the generator must inline the referenced operation's permission/scope metadata while keeping the dependent operation's own declared `resourceKeyTemplate`; see `authentication-and-authorisation/plan.md` for the full auth metadata model.
- Success/response contract metadata that captures what legacy `IReturn<TResponse>` and host conventions relied on, so downstream modules can reason about response bodies, error shapes, and serializer needs.
- Flattened operation outcomes that expand `Results<T1, ...>` into concrete per-outcome descriptor metadata while keeping the raw wrapper as a generator-internal concern.
- Descriptor metadata for same-status outcome groups when a compile-time-safe discriminator exists, especially generated `eventId`-based resource-event groups.
- Descriptor metadata should be rich enough to drive auth/access build artifacts and generated test inputs without separate runtime rediscovery passes.

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
- Let descriptor generation carry explicit auth/access metadata and provenance forward rather than forcing runtime authorisers or external tools to rediscover it.

## Sequencing

Implement after the foundation module system and before any module that depends on host or descriptor feature data.

## Durable progress

- Live active/next/last/gate tracking for this workstream lives only in `..\progress.json`. This durable-progress section records rationale, completed-slice history, and acceptance notes; it is not the live tracker.
- The `Phase 0 exit` human review gate is closed, so this workstream is now the active Phase 1 implementation target.
- First active slice for this workstream: promote the already-approved operation-discovery facts (`kind`, `namespace`, and `typeName`) into typed `ApiOperationDescriptor` feature data via the existing operation-discovery module, then prove that behavior through descriptor-generation output and a runtime-facing generated `Configure()` assertion before broader host/result/auth discovery work.
- Completed first discovery-metadata slice: `OperationDiscoveryManifestModule` now also acts as an `IDescriptorContributor`, generated descriptors set `OperationDiscoveryFeatureData` with the same approved `kind`/`namespace`/`typeName` facts already emitted by the manifest path, `OperationDescriptorSourceGenerator` now wires that contributor into normal descriptor generation, and the slice is covered by focused generator assertions plus runtime-facing generated `Configure()` integration tests.
- Completed first host-aware inclusion slice: `LinkGenerationModule` now records shared HTTP host-exposure planning metadata whenever descriptor planning emits operation URLs, `OperationDiscoveryManifestModule` projects that into typed `OperationHostExposureFeatureData`, and `BlueprintApp` now indexes registered descriptors by host through `GetOperationsForHost(...)` without reintroducing runtime discovery. Focused generator assertions plus shared-app integration tests prove HTTP-exposed operations are available through that host index while internal-only operations remain absent from it, and the current build plus generator/codewriter/integration test matrix is green after the reviewed self-link snapshot update.
- Completed first discovery-manifest host-exposure slice: `DebugManifestPlanBuilder` now replays descriptor contributors before debug-manifest contributors, `DebugManifestContributorContext` exposes descriptor metadata through `GetMetadata<T>()`, and `OperationDiscoveryManifestModule` now projects ordered `hostExposure` arrays into the canonical `operations` manifest when compile-time host metadata exists. Focused module tests pin the explicit host-exposure assertion plus a reviewed manifest snapshot so inspection surfaces stay aligned with the existing descriptor/app-registration host metadata without reparsing link attributes.
- Completed first enabled-host diagnostic slice: `IEnabledHostContributor` now exposes the enabled-host registry seam without breaking existing host contributors, `LinkGenerationModule` is the first live HTTP host contributor, `HostExposureRegistration` preserves declaration locations without making them part of structural equality, and shared descriptor/debug-manifest planning now reports `BPOD001` when an operation records host exposure that no enabled host contributor supports. Focused module tests cover the failing unsupported-host proof plus the cacheability-equality guard, the existing descriptor and `BlueprintApp` host-exposure proofs still pass, and the current build plus generator/codewriter/integration test matrix is green after the docs/skill updates.
- Next recommended follow-on: once a second concrete host contributor exists, use the same enabled-host registry to add the first conflicting-host exposure diagnostic instead of guessing conflict semantics from HTTP-only data. A core-owned non-HTTP host is an acceptable source for that second contributor, but keep its namespaces and public type names obviously host-specific so the later conflict rules stay unambiguous.
- Approved prerequisite before that conflicting-host follow-on: add the smallest real core-owned tasks host contributor, using the legacy `IBackgroundTask` shape as the host-specific marker but limiting scope to compile-time host exposure metadata plus `BlueprintApp` host indexing. Do not broaden this prerequisite into scheduler abstractions, recurring-task metadata, task flushing, or other runtime background-task behavior; those remain owned by `../background-tasks/plan.md`.
- Completed tasks-host prerequisite slice: `Blueprint.Tasks.IBackgroundTask` now provides the host-specific marker contract, `TasksHostModule` is the second concrete `IEnabledHostContributor`, generated descriptors/debug manifests now project ordered `tasks` host exposure through the existing operation-discovery seam, and `BlueprintApp.GetOperationsForHost("tasks")` now proves the runtime host index consumes that metadata without re-deriving task intent at runtime. The current verification matrix (`dotnet build .\Blueprint.sln --no-restore`, `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --no-restore`, `dotnet test .\src\Blueprint.SourceGenerator.CodeWriter.Tests\Blueprint.SourceGenerator.CodeWriter.Tests.csproj --no-restore`, and `dotnet test .\src\Blueprint.SourceGenerator.Tests.Integration\Blueprint.SourceGenerator.Tests.Integration.csproj --no-restore`) is green after the reviewed docs/plan updates.
- Completed the previously approved conflicting-host follow-on: shared descriptor/debug-manifest planning now reports `BPOD002` when one operation records exposure to more than one enabled host contributor, keeping the enabled-host registry as the single compile-time source of truth for host ownership and failing before descriptor or manifest projection continues. Focused module and descriptor-generator tests pin the multi-host failure path while the existing host-ordering/cacheability guards still pass, and the verification matrix (`dotnet format .\Blueprint.sln --no-restore --include src\Blueprint.SourceGenerator\Modules\OperationDiscoveryManifest\HostExposureDiagnostics.cs src\Blueprint.SourceGenerator\Modules\OperationDiscoveryManifest\OperationDiscoveryDiagnosticDescriptors.cs src\Blueprint.SourceGenerator\Planning\DebugManifestPlanBuilder.cs src\Blueprint.SourceGenerator\Generators\Descriptors\OperationDescriptorSourceGenerator.cs src\Blueprint.SourceGenerator.Tests\Modules\OperationDiscoveryManifestModuleTests.cs src\Blueprint.SourceGenerator.Tests\Generators\Descriptors\OperationDescriptorSourceGeneratorTests.HostExposure.cs`, `dotnet build .\Blueprint.sln --no-restore`, `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --no-restore`, `dotnet test .\src\Blueprint.SourceGenerator.CodeWriter.Tests\Blueprint.SourceGenerator.CodeWriter.Tests.csproj --no-restore`, and `dotnet test .\src\Blueprint.SourceGenerator.Tests.Integration\Blueprint.SourceGenerator.Tests.Integration.csproj --no-restore`) is green after the reviewed docs/skill/tracking updates.
- Next approved handoff: move to `../pipeline-composition-and-execution/plan.md` for the first activation slice that routes `HandlerExecutionModule` through the activated seam, while keeping the paired workstream-7 design loop explicit and preserving the current result-wrapping behavior for that slice so the semantic result contract does not freeze accidentally.

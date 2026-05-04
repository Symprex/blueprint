# Messaging and integration hosts

- Status: Planned
- Priority: P3
- Classification: Core and consumer-space host seam / alternate host
- Depends on: `../foundation-module-system/plan.md`, `../operation-discovery-and-descriptors/plan.md`, `../pipeline-composition-and-execution/plan.md`, `../background-tasks/plan.md`

## Why this module exists

The legacy ecosystem supports more than HTTP:

- alternate hosts
- message-driven execution models
- bus integrations such as Rebus
- consumer-specific pipeline extensions

Signature365 proves that these seams are real and valuable. The new rewrite must expose them intentionally rather than leaving consumers to fork the generator.

## Evidence

- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\Middleware\IOperationExecutorBuilderScanner.cs`
- Signature365 extension: `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common.Core\Api\OperationsHandlerScanner.cs`
- Signature365 extension: `D:\Projects\Signature365\src\Core\Symprex.Signature365.BlueprintRebus\RebusHandleMessageOperationExecutorBuilderScanner.cs`
- Signature365 extension: `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common\Api\SetTenantMiddleware.cs`
- Signature365 extension: `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common\Api\ResourceExpandMiddleware.cs`

## Migration goal

Provide explicit, public seams for alternate hosts and handler-discovery strategies, while deciding what belongs in:

- core Blueprint
- official companion packages
- consumer-owned generator packages

## Runtime support to build

- alternate host registration model
- alternate handler-source registration model
- per-host descriptor metadata
- optional message/consumer runtime helpers
- package hooks for consumer-specific pipeline contributors

## Consumer configuration and activation

This plan should enable:

- package-provided handler scanners
- package-provided host exposure rules
- consumer-owned source-generator packages that compose `BlueprintSourceGenerationConfig`
- thin runtime builder helpers that activate the relevant modules/services

The generator composition root must be public and stable enough for consumers to build on.

## Worked example expectation

A consumer should be able to migrate a Signature365-style extension in one of two supported ways:

1. the preferred path: ship a thin generator package that composes official and custom modules
2. the fallback path: register a constrained runtime middleware contribution in a known stage when full generator participation is not justified

The preferred path should be able to express examples such as:

- Rebus message-consumer discovery
- a tenant/context enrichment step
- package-provided message transaction coordination

## Compile-time checks to add

- alternate host enabled without compatible operation metadata
- multiple handler sources producing illegal ambiguity
- message-handler covariance/contravariance rules that would change semantics unexpectedly
- consumer package uses module attributes without including the matching generator module

## Observability and debuggability

- manifest of active modules/packages
- handler-source provenance per operation
- host exposure summary
- diagnostics that explain package ordering and activation failures

## Explicit decisions

- Treat `ApiOperations` / `OperationsHandlerScanner` behavior as core and move it out of this extension bucket.
- Rebus integration should be treated as consumer-space or companion-package functionality, not core Blueprint.
- A generally reusable non-HTTP host may live in the core Blueprint package when it represents a stable product capability rather than a package-specific integration.
- Keep non-HTTP hosts obviously separate from HTTP in namespaces and public type names so host-specific concerns do not blur together inside the core package.
- `SetTenantMiddleware` is evidence for required hooks, not evidence that tenant logic itself belongs in core.
- Consumer-owned generator packages are a supported scenario and must be designed for, not tolerated accidentally.
- A constrained runtime middleware fallback is acceptable as an escape hatch, but it is not the default extension model.

## Sequencing

Implement after the core host and pipeline model exists; package-specific integrations can then build on stable seams.

## Durable progress

### Completed proof slices

1. **consumer-pipeline-composition-proof** (commit 14e09c3) — proved companion generator packages can compose `BlueprintSourceGenerationConfig` with custom pipeline modules via `OperationExecutorPipelineSourceGenerator` inheritance
2. **descriptor-contributor-composition-proof** (commit a0ed4a7) — proved consumer `IDescriptorContributor` can extend descriptor metadata via companion `OperationDescriptorSourceGenerator` inheritance
3. **consumer-host-contributor-proof** (commit fe3444d) — proved consumer `IEnabledHostContributor` can register alternate host exposure via companion generator, including diagnostic validation (BPOD001/BPOD002)
4. **dual-companion-generator-integration-proof** (commit 1d47a1e) — proved both companion generators (descriptor + pipeline) compose in same compilation without conflicts, with conditional pipeline contribution based on marker interface

### Key findings from proofs

- Host-contributing modules MUST use `BlueprintModuleCategory.Core` (not Consumer) because `OperationDiscoveryManifestModule` (Core, Order=100) reads host-exposure metadata during descriptor contribution
- `ContributorRegistrationComparer` sorts by Category first (Core=0, Consumer=1), then Order, then ContributorId — Consumer modules ALWAYS run after ALL Core modules
- Pipeline modules can conditionally contribute by checking `context.Operation.TypeSymbol.AllInterfaces` for marker interfaces
- `HostExposureRegistration` accepts arbitrary string host names — no factory method needed
- Runtime `BlueprintApp.GetOperationsForHost()` is completely host-name-agnostic (reads `OperationHostExposureFeatureData`)
- `AppDataModelSourceGenerator` is sealed with internal config constructor — consumer companion packages cannot extend app-model generation; the pipeline is: companion descriptor generator adds host exposure → default app generator registers all operations → runtime partitions by feature data

### Remaining production features (not yet started)

- Pluggable handler discovery for alternative handler patterns (e.g., Rebus `IHandleMessages<T>`)
- Constrained runtime middleware fallback escape hatch
- Compile-time diagnostic: consumer package uses module attributes without including matching generator module
- Active modules/packages manifest section
- Handler-source provenance per operation in debug manifest
- NuGet packaging guidance for shipping companion generators

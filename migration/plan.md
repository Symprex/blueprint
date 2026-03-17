# Blueprint source-generated migration plan

## Purpose

This is the working migration tracker for bringing the legacy Blueprint library at `D:\Projects\Signature365\src\Blueprint` across to the source-generated rewrite in this repository.

The target is **consumer-contract compatibility**, not byte-for-byte pipeline parity. Generated executors may look very different from the legacy frame-based output so long as runtime behavior remains compatible and the new implementation is more deterministic, analyzable, and AOT-friendly.

## Non-negotiables

- Keep pushing work from runtime into source generation, analyzers, and deterministic build-time models.
- Do not re-introduce runtime C# generation + compilation.
- Preserve the important consumer-facing shapes: commands, queries, resources, HTTP attributes, auth/validation attributes, and operation handling semantics, while allowing low-value builder and activation APIs to change when a mechanical migration path exists.
- Prefer compile-time diagnostics over runtime failures whenever the generator can know the answer.
- Treat compile-time, IDE/build, and runtime scale as first-class architecture concerns and protect them with deterministic harnesses.
- Treat observability, generated inspection surfaces, and AI-friendly diagnostics as first-class deliverables.
- Avoid rebuilding the old frame system wholesale; use `Blueprint.SourceGenerator.CodeWriter` and intermediate planning models instead.
- Prefer compile-time defaults plus thin configuration hooks over runtime feature toggles when a capability is effectively always on in real applications.
- Optimize first for Symprex's migration path, while keeping the seams generic enough that Blueprint remains usable outside Symprex.

## Evidence base

### Current source-generated repository

- `src\Blueprint.SourceGenerator\Modules\IBlueprintSourceGeneratorModule.cs`
- `src\Blueprint.SourceGenerator\Modules\LinkGenerationModule.cs`
- `src\Blueprint.SourceGenerator\Generators\Descriptors\OperationDescriptorSourceGenerator.cs`
- `src\Blueprint.SourceGenerator\Generators\Pipelines\CodeGen\PipelineCodeGenerator.cs`
- `src\Blueprint.SourceGenerator\Generators\ApiResources\ApiResourceSourceGenerator.cs`

### Legacy Blueprint core

- `D:\Projects\Signature365\src\Blueprint\Blueprint\IMiddlewareBuilder.cs`
- `D:\Projects\Signature365\src\Blueprint\Blueprint\MiddlewareBuilderContext.cs`
- `D:\Projects\Signature365\src\Blueprint\Blueprint\Configuration\PipelineBuilder.cs`
- `D:\Projects\Signature365\src\Blueprint\Blueprint\Configuration\BlueprintApiBuilder.cs`
- `D:\Projects\Signature365\src\Blueprint\Blueprint\Configuration\IOperationScannerConvention.cs`
- `D:\Projects\Signature365\src\Blueprint\Blueprint\Middleware\IOperationExecutorBuilderScanner.cs`
- `D:\Projects\Signature365\src\Blueprint\Blueprint.Http\HttpHost.cs`
- `D:\Projects\Signature365\src\Blueprint\Blueprint\CodeGennedExecutor.cs`
- `D:\Projects\Signature365\src\Blueprint\Blueprint.Http\Middleware\ResourceEventHandler.cs`

### Signature365 extension examples

- `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common.Core\Api\OperationsHandlerScanner.cs`
- `D:\Projects\Signature365\src\Core\Symprex.Signature365.BlueprintRebus\RebusHandleMessageOperationExecutorBuilderScanner.cs`
- `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common\Data\Marten\SaveMartenChangesMiddleware.cs`
- `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common\Data\Marten\UseMartenAsOutboxMiddleware.cs`
- `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common\Api\ResourceExpandMiddleware.cs`
- `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common\Api\SetTenantMiddleware.cs`
- `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common.Core\Authorisation\ResourceAttribute.cs`
- `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common.Core\Authorisation\FromResourceAttribute.cs`

## The biggest architectural gap to close

The current rewrite has a **descriptor-oriented module hook**. The legacy system has a **full pipeline composition model** with stages, matching rules, variable discovery, exception/finally hooks, host conventions, handler scanners, and a shared execution story that was spread across generated code rather than modeled explicitly.

The migration therefore is not just "implement more modules". It requires a cross-cutting redesign so modules can contribute to:

1. operation discovery and host inclusion
2. descriptor metadata and diagnostics
3. pipeline composition and stage ordering
4. variable discovery and reusable local/service/context values
5. a shared executor shell for activity, logging, exception translation, and cleanup
6. post-execution processing
7. optional runtime services and consumer-space extension packages
8. deliberate breaking changes plus automated migration tooling

That redesign starts in [`foundation-module-system/plan.md`](foundation-module-system/plan.md) and is made safe to adopt through [`breaking-changes-and-migration-tooling/plan.md`](breaking-changes-and-migration-tooling/plan.md).

## Recommended phased order

### Phase 0 - Foundation, migration safety, and scale

1. [`foundation-module-system`](foundation-module-system/plan.md)
2. [`breaking-changes-and-migration-tooling`](breaking-changes-and-migration-tooling/plan.md)
3. [`performance-and-scale`](performance-and-scale/plan.md)
4. [`generated-manifests-and-inspection-surfaces`](generated-manifests-and-inspection-surfaces/plan.md)

### Phase 1 - Core parity path

5. [`operation-discovery-and-descriptors`](operation-discovery-and-descriptors/plan.md)
6. [`pipeline-composition-and-execution`](pipeline-composition-and-execution/plan.md)
7. [`result-model-and-host-translation`](result-model-and-host-translation/plan.md)
8. [`error-model-and-exception-handling`](error-model-and-exception-handling/plan.md)
9. [`http-host-and-message-population`](http-host-and-message-population/plan.md)
10. [`validation`](validation/plan.md)
11. [`authentication-and-authorisation`](authentication-and-authorisation/plan.md)
12. [`auditing-and-observability`](auditing-and-observability/plan.md)
13. [`reference-app-and-end-to-end-example`](reference-app-and-end-to-end-example/plan.md) - start Layer 1 once the core HTTP path is runnable; later layers close after Phase 2 capabilities land

### Phase 2 - Resource and API shape parity

14. [`resource-model-links-and-post-processing`](resource-model-links-and-post-processing/plan.md)
15. [`openapi`](openapi/plan.md)

### Phase 3 - Non-HTTP and consumer-space capability

16. [`background-tasks`](background-tasks/plan.md)
17. [`persistence-and-transaction-coordination`](persistence-and-transaction-coordination/plan.md)
18. [`messaging-and-integration-hosts`](messaging-and-integration-hosts/plan.md)

## Module map

| Module | Priority | Classification | Why it exists |
| --- | --- | --- | --- |
| [`foundation-module-system`](foundation-module-system/plan.md) | P0 | Core architecture | Replaces the legacy frame/middleware composition model with a source-generator-native module model. |
| [`breaking-changes-and-migration-tooling`](breaking-changes-and-migration-tooling/plan.md) | P0 | Cross-cutting rollout | Makes deliberate breaking changes explicit and ensures they are backed by analyzers, code fixes, and migration reporting. |
| [`performance-and-scale`](performance-and-scale/plan.md) | P0 | Cross-cutting engineering system | Protects compile-time, IDE/build, and runtime behavior with deterministic scale harnesses plus scheduled benchmarks. |
| [`generated-manifests-and-inspection-surfaces`](generated-manifests-and-inspection-surfaces/plan.md) | P0 | Cross-cutting architecture support | Defines the common contract for generated manifests, build artifacts, and runtime inspection surfaces. |
| [`operation-discovery-and-descriptors`](operation-discovery-and-descriptors/plan.md) | P1 | Core | Reintroduces host-aware operation discovery, feature data, descriptor enrichment, and app registration. |
| [`pipeline-composition-and-execution`](pipeline-composition-and-execution/plan.md) | P1 | Core | Replaces the monolithic pipeline generator with an ordered execution plan plus a shared executor shell. |
| [`result-model-and-host-translation`](result-model-and-host-translation/plan.md) | P1 | Core/result | Defines the host-agnostic result model, typed unions, and the translation boundary between semantic results and active hosts such as HTTP. |
| [`error-model-and-exception-handling`](error-model-and-exception-handling/plan.md) | P1 | Core | Makes success/failure contracts, exception translation, and documented error responses explicit instead of hard-coded. |
| [`http-host-and-message-population`](http-host-and-message-population/plan.md) | P1 | Core host | Rebuilds HTTP exposure, input binding, result execution, and serializer-aware request/response handling. |
| [`validation`](validation/plan.md) | P1 | Core middleware | Moves validation toward generated, DataAnnotations-first plans while keeping only a focused compatibility subset of Blueprint-specific validators. |
| [`authentication-and-authorisation`](authentication-and-authorisation/plan.md) | P1 | Core middleware | Restores authentication, user-context loading, and authorisation semantics with compile-time resource-permission analysis. |
| [`auditing-and-observability`](auditing-and-observability/plan.md) | P1 | Cross-cutting | Makes logs, traces, audits, and generated inspection surfaces part of the baseline architecture. |
| [`resource-model-links-and-post-processing`](resource-model-links-and-post-processing/plan.md) | P2 | Core/resource | Covers `ApiResource` metadata, links, resource events, and resource-aware post-processing. |
| [`openapi`](openapi/plan.md) | P2 | Core HTTP capability | Rebuilds OpenAPI generation from the same source-generated descriptors and serializer metadata that power the HTTP host. |
| [`reference-app-and-end-to-end-example`](reference-app-and-end-to-end-example/plan.md) | P1 | Cross-cutting reference surface | Defines the curated runnable sample that demonstrates the intended consumer story and doubles as an end-to-end regression surface. |
| [`background-tasks`](background-tasks/plan.md) | P3 | Optional host/module | Restores background-task scheduling and execution as a first-class non-HTTP host. |
| [`persistence-and-transaction-coordination`](persistence-and-transaction-coordination/plan.md) | P3 | Consumer-space integration seam | Provides the generic hooks needed for save-changes, transaction scopes, and outbox patterns without making Marten or another store a core dependency. |
| [`messaging-and-integration-hosts`](messaging-and-integration-hosts/plan.md) | P3 | Consumer-space extension seam | Reintroduces alternate hosts and package-owned integrations such as Rebus-style message handling without forcing them into core Blueprint. |

## Tracking checklist

- [ ] Foundation module system and pipeline-planning abstractions agreed
- [ ] Breaking changes and migration-tooling strategy agreed
- [ ] Compile-time, IDE/build, and runtime scale strategy agreed
- [ ] Generated manifest and inspection-surface contract agreed
- [ ] Operation discovery, host filtering, and descriptor feature model implemented
- [ ] Execution planning supports ordered stage contributions, a shared executor shell, and the replacement for general nested execution
- [ ] Shared host-agnostic result model, typed unions, and host translation boundary agreed
- [ ] Error model, exception translation, and declared failure responses agreed
- [ ] HTTP host and binding parity reached
- [ ] Reference app Layer 1 is runnable with real handlers
- [ ] Validation parity reached
- [ ] Authentication and authorisation parity reached
- [ ] Auditing, logging, tracing, and generated debug surfaces agreed
- [ ] Resource metadata, link generation, and no-nested resource-event contract agreed
- [ ] OpenAPI generation strategy agreed
- [ ] Reference app and end-to-end consumer story agreed
- [ ] Background task host strategy agreed
- [ ] Persistence/transaction seams agreed
- [ ] Messaging/integration host seams agreed

## Explicit design decisions already made

- **Do not** port `FramesCollection`, `Frame`, `SyncFrame`, and `AsyncFrame` as the dominant authoring model.
- **Do** preserve the useful behaviors those types enabled: ordered stage contributions, variable reuse, and try/catch/finally participation.
- Use `Blueprint.SourceGenerator.CodeWriter` for final rendering, but keep most module contributions declarative until the final write step.
- Keep module composition explicit through configuration rather than implicit assembly scanning.
- Treat `ApiOperations` / `OperationsHandlerScanner` behavior as core functionality, not as an optional example package.
- Prefer compile-time markers, conventions, and generated defaults over preserving legacy fluent activation APIs one-to-one.
- Treat HTTP as implying OpenAPI. `ConfigureOpenApi` may remain, but `AddOpenApi` should not be the long-term activation switch.
- Treat auth, authorisation, and link generation as always architecturally available capabilities that compile away when unused. Keep explicit configuration hooks only for behavior that cannot be inferred statically.
- Prefer generated auth/access descriptors, generated binders, and generated access manifests over runtime regex/reflection template expansion and separate access-report tools.
- Do not treat general nested execution parity as a goal. First redesign resource-event self-query and only add a narrow internal re-query path if a simpler model cannot preserve the required behavior.
- Keep the shared result model host agnostic. Typed results such as `OkResult<T>` remain part of the public contract, while hosts such as HTTP translate semantic results plus attachable hints such as `ETag` and cache control. See [`result-model-and-host-translation/plan.md`](result-model-and-host-translation/plan.md).
- Resource events use concrete contract-level result types, typed one-of-many response contracts, and an explicit separation between event body and event identity. See [`resource-model-links-and-post-processing/plan.md`](resource-model-links-and-post-processing/plan.md).
- Keep the human-facing reference app small and curated; use synthetic generated fixtures for large-scale benchmark and stress scenarios.
- Keep generated manifests and inspection surfaces machine-readable and canonical so runtime tooling, CI, and AI agents can all project from the same underlying data.
- Treat `ResourceExpand`, Rebus, Marten, and DB-backed ownership/resource-key expansion as consumer-space concerns. Their existence proves the seam, not the core package boundary.

## Open questions to resolve during implementation

- What is the smallest public authoring API that still makes consumer-owned generator packages practical?
- How thin can the remaining builder/configuration surface become without making migration ergonomically worse than necessary?
- Which legacy validation attributes should remain as compatibility aliases, and which should be analyzer-guided migrations to DataAnnotations or custom consumer validators?
- How much constrained runtime middleware fallback is worth supporting for consumer-only extensions that cannot justify a full generator package?
- What is the canonical `JsonSerializerContext` ownership, aggregation, and validation story across contracts, hosts, and companion packages?
- How should `IQuery<T>` / `ICommand<T>` / `IReturn<T>` metadata interact with `Results<T1, ...>` so descriptors, OpenAPI, and generated clients all see the expanded contract consistently?

## Success criteria

The migration plan is only successful if it leaves us with:

- a clear architectural route from the current descriptor-only module hook to a full source-generated module system
- a prioritized, dependency-aware module inventory
- explicit package-boundary decisions for core vs optional vs consumer-specific functionality
- a plan for deliberate breaking changes backed by automated migration tooling
- a plan for deterministic scale and performance feedback before Signature365-sized migrations begin
- a realistic reference app target that demonstrates the intended consumer experience end to end
- a plan for deterministic diagnostics, test harnesses, and generated debug surfaces alongside each module

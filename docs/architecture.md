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

### 6. Runtime executes precomputed metadata

At runtime:

- `BlueprintApp` stores registered operations and links (`src\Blueprint.Runtime.Model\BlueprintApp.cs`)
- `BlueprintAppExecutor` uses generated pipeline registrations (`src\Blueprint.Runtime\Exec\BlueprintAppDataModel.cs`)
- `MapBlueprintApi<TApp, TExec>()` maps the generated surface into ASP.NET Core endpoints (`src\Blueprint.Runtime\Http\ApplicationBuilderExtensions.cs`)

The important architectural rule is that runtime code consumes generated knowledge. It should not re-scan the compilation model or re-derive contracts that the generators already knew.

## Modules are the extension seam

`IBlueprintSourceGeneratorModule` is the main extension point for new compile-time features (`src\Blueprint.SourceGenerator\Modules\IBlueprintSourceGeneratorModule.cs`).

Each module can:

- register marker attributes
- inspect and validate discovered models
- emit descriptor code

`LinkGenerationModule` is the model example:

- it injects root/self link marker attributes
- parses those attributes during configuration
- validates placeholder and resource-type usage
- emits descriptor code that adds links and URLs

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

At this stage post-initialization, descriptor, and pipeline planning/validation seams are executed end-to-end in descriptor generation, and the executor-pipeline generator routes validation-stage DataAnnotations/IValidatableObject checks through `ValidationModule` plus execution-stage handler invocation through `HandlerExecutionModule` while preserving the previous validation catch/result behavior and handler result wrapping. `ValidationModule` now plans property validation independently: supported property-level DataAnnotations families (`Required`, `MaxLength`, `MinLength`, `StringLength`, and `EmailAddress`) render generated fast-path checks through extracted strategy helpers, unsupported property shapes use the shared `Blueprint.Validation.ValidationRuntime.ValidateProperty(...)` fallback against `ApiOperationDescriptor.GetPropertyAttributes(...)`, and type-level validation attributes plus `IValidatableObject` remain on the existing whole-object `Validator.TryValidateObject(...)` fallback path for now. Pipeline render steps now carry contributor-owned rendering callbacks with no separate behavior-driving enum, stage participation is inferred implicitly when contributors register render steps or variables, and descriptor/pipeline/render contexts expose helper methods plus only the small read-only metadata properties needed for clear module code instead of raw public access to mutable plan, handler, or writer objects. `PipelineCodeGenerator` only replays ordered steps instead of interpreting validation or handler-execution markers centrally. Logging, exception/result handling, host, and app-registration contributors are still deliberately scaffolded or hard-coded so later migration slices can fill them in without reinventing the foundation contract. The debug-manifest seam is now activated through `BlueprintSourceGenerationConfig.BuildDebugManifest(...)`, `DebugManifestContributorContext`, `OperationDiscoveryManifestModule`, and `OperationsDiscoveryManifestEmitter` for the first operations/discovery payload, and debug-manifest entry keys are now contributor-owned too: when a later contributor reuses a claimed key, `BuildDebugManifest(...)` fails deterministically with `BPMOD006` instead of silently replacing the earlier entry. The same `OperationDiscoveryManifestModule` now also seeds generated descriptors with typed `OperationDiscoveryFeatureData` carrying the approved `kind`, `namespace`, and `typeName` facts, so later runtime and Phase 1 contributors can read those discovery facts from `ApiOperationDescriptor` without rediscovering them separately. Richer manifest families and runtime projections remain follow-on work. Runtime validation hot-path measurement now lives in `src\Blueprint.SourceGenerator.Benchmarks`.

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

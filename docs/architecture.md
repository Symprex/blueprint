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
- `src\Blueprint.SourceGenerator\Modules\LinkGenerationModule.cs`

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
- `IPipelineContributor` now exposes a typed `Contribute(PipelineContributorContext)` seam, and descriptor generation runs `ConfigurePipelinePlan()` to build pipeline planning state and report `BPMOD004` for unsatisfied required variables

At this stage post-initialization, descriptor, and pipeline planning/validation seams are executed end-to-end in descriptor generation. The executor-pipeline generator is not yet routed through module planning, and host, app-registration, plus debug-manifest contributors are still deliberately scaffolded so later migration slices can fill them in without reinventing the foundation contract.

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

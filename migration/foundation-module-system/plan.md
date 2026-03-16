# Foundation module system

- Status: Planned
- Priority: P0
- Classification: Core architecture
- Depends on: none

## Why this module exists

The current source-generated rewrite only lets modules influence descriptor generation through `IBlueprintSourceGeneratorModule`. The legacy system used `IMiddlewareBuilder`, `MiddlewareBuilderContext`, `IOperationScannerConvention`, `IBlueprintHost`, and `IOperationExecutorBuilderScanner` to shape the full execution pipeline.

Without a new foundation layer, every later migration module will either hard-code behavior into the pipeline generator or recreate the old frame system piecemeal.

## Evidence

- Current: `src\Blueprint.SourceGenerator\Modules\IBlueprintSourceGeneratorModule.cs`
- Current: `src\Blueprint.SourceGenerator\Generators\Descriptors\OperationDescriptorSourceGenerator.cs`
- Current: `src\Blueprint.SourceGenerator\Generators\Pipelines\CodeGen\PipelineCodeGenerator.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\IMiddlewareBuilder.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\MiddlewareBuilderContext.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\Configuration\PipelineBuilder.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\Configuration\BlueprintApiBuilder.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint\CodeGennedExecutor.cs`
- Legacy: `D:\Projects\Signature365\src\Blueprint\Blueprint.Http\Middleware\ResourceEventHandler.cs`

## Migration goal

Introduce a source-generator-native composition model that keeps the strengths of the legacy pipeline builder while avoiding the complexity of the old frame graph.

## Target design

### 1. Evolve `IBlueprintSourceGeneratorModule`

Do not grow it into one giant interface. Keep a small module identity/root contract and add capability interfaces, for example:

- post-initialization marker contributors
- operation discovery contributors
- descriptor metadata contributors
- pipeline contributors
- host contributors
- app-registration contributors
- debug/manifest contributors

This keeps modules simple and lets each feature opt into only the hooks it actually needs.

### 1a. Concrete hook shape to plan against

The implementation work should assume a concrete capability split along these lines:

- `IBlueprintSourceGeneratorModule`
  - module identity
  - ordering/category metadata
  - optional package/runtime configuration metadata
- `IDescriptorContributor`
  - enriches operation/descriptor models
  - adds diagnostics
  - contributes feature data
- `IPipelineContributor`
  - contributes ordered steps to one or more pipeline stages
  - declares consumed and produced variables
- `IHostContributor`
  - decides whether a host supports an operation
  - contributes host-specific metadata such as HTTP method, task schedule, or message subscription details
- `IAppRegistrationContributor`
  - contributes generated app/runtime registration code or metadata
- `IDebugManifestContributor`
  - contributes human/AI inspection data

This is a planning sketch, not a locked API, but later module plans should assume hooks of roughly this shape exist.

### 2. Introduce intermediate planning models

Modules should mostly contribute to plans, not directly write ad-hoc strings:

- `OperationModel` or equivalent compile-time view of an operation
- `DescriptorPlan` for feature data, URLs, responses, auth requirements, validation plan, etc.
- `ExecutionShellPlan` for shared activity/logging/exception/finally behavior
- `PipelinePlan` for ordered stage contributions
- `AppRegistrationPlan` for app/host registration
- `GeneratedDebugManifest` for human/AI inspection

`CodeWriter` should render the final plan; it should not be the only data model.

### 2a. What replaces the legacy compiler/frame infrastructure

The legacy `Blueprint.Compiler` stack provided:

- generated type/method/assembly models
- frame ordering/composition
- variable sources
- runtime compilation/debug helpers

The rewrite should replace that stack with:

- Roslyn incremental generators for compilation
- the vendored `Blueprint.SourceGenerator.CodeWriter` for final rendering
- explicit planning models (`DescriptorPlan`, `PipelinePlan`, `HostPlan`, manifest models)
- module-declared variables and stage contributions instead of an implicit frame graph
- debug manifests and generated source snapshots instead of runtime `WhatCodeDidIGenerate()`-style introspection

### 2b. Declarative vs custom pipeline contributions

The target should be a **hybrid** model:

- common contributions use declarative step records so they remain analyzable
- uncommon cases may use a constrained `CodeWriter` callback escape hatch
- even escape-hatch contributions must declare:
  - variables consumed
  - variables produced
  - stage placement
  - whether they belong to the shared executor shell or per-operation core plan
  - diagnostics they can emit

This keeps the system flexible without losing explainability.

### 3. Reintroduce ordered pipeline stages

Preserve the legacy mental model because it matches real cross-cutting behavior:

- Setup
- Population
- Authentication
- Authorisation
- Validation
- Execution
- PostExecution
- Cleanup

Modules must be able to contribute ordered steps to these stages with deterministic ordering.

If resource-event self-query survives the redesign, it should be modeled as an explicit internal re-query facility owned by the relevant module plan, not as a general "every contributor must understand nested execution" requirement.

### 4. Add a typed variable registry

The replacement for frame-based variable discovery should be a typed registry rather than raw string concatenation.

Modules need to be able to request or register:

- the operation instance
- known context values
- services resolved from DI
- generated locals
- values produced by other modules
- optional values with explicit fallback rules

That registry should also carry lifetime/scope information so a value can become a constructor field, a local, or a context lookup depending on availability and cost.

### 4a. Cross-module variable sharing is a hard requirement

The registry must explicitly support one module producing a value and another consuming the same value later.

Planning assumption:

- variables are registered by typed keys plus purpose, for example:
  - current operation
  - `ClaimsIdentity`
  - `IDocumentSession`
  - `RebusTransactionScope`
  - operation result
- producers register how the value is created and at which stage it becomes available
- consumers declare whether the dependency is required or optional
- duplicate producers for a unique variable become a compile-time error unless explicitly allowed

Worked example:

- a transaction module produces `RebusTransactionScope` at setup/execution entry
- a persistence module requires `IDocumentSession`
- an outbox module requires both `IDocumentSession` and the optional `RebusTransactionScope`
- the final pipeline plan ensures all three steps refer to the same generated locals/fields instead of each module inventing its own lookup

### 4b. DI lifetime optimization needs an explicit replacement

The generator cannot inspect a live `IServiceCollection` the way legacy runtime compilation could.

Initial plan:

- default to generated `GetRequiredService<T>()` lookups when no better information exists
- allow modules or activation helpers to provide explicit lifetime hints for safe constructor-field capture
- optimize known framework or module-owned services deliberately, not by trying to recreate runtime container inspection inside the generator

This is simpler, deterministic, and AOT-friendly.

### 5. Support exception and finally participation

Legacy middleware could register exception handlers and finally frames. The new model needs explicit hooks for:

- specialized exception translation
- post-failure observability
- guaranteed cleanup/finalization
- controlled participation in the shared executor shell

### 5a. Hosts are a first-class concept

Do not leave host modeling as an open architectural question.

Decision for planning:

- hosts are first-class concepts composed from modules
- a host contributes operation inclusion rules plus host-specific descriptor/pipeline metadata
- modules can assist a host, but "is this operation part of the HTTP host?" should not be an accidental side effect of unrelated descriptor contributors

This gives HTTP, background tasks, and message-host integrations a stable place in the design.

### 6. Keep module composition explicit

The new system should be configured through an explicit module list/config object rather than assembly scanning. This is important for:

- deterministic source generation
- testability
- consumer-defined source-generator packages
- AI-friendly inspection of what is active

## Runtime support to build

- A pipeline runtime contract that can carry the generated stage outputs without reflection-heavy discovery.
- A shared executor shell/runtime helper so activity, logging, exception translation, and cleanup logic are not regenerated ad hoc in every pipeline.
- Optional internal re-query support for resource events if the redesign still requires it.
- Strongly-typed descriptor feature storage instead of unstructured metadata objects wherever possible.
- A lightweight runtime manifest or debug surface that reports the applied modules/stages for each operation.

## Consumer configuration and package model

- Keep `BlueprintSourceGenerationConfig` as the explicit composition root.
- Support thin, consumer-owned generator packages that assemble custom modules on top of the base generator.
- Consider a reusable package for composing modules and exposing a stable public authoring API for module authors.
- Prefer builder helpers that configure modules and register runtime services; do not depend on runtime scanning to discover modules.
- Treat legacy activation calls for effectively always-on capabilities as migration inputs, not as the target architecture.
- Consider a constrained runtime middleware fallback for consumer-only extensions that cannot justify a custom generator package, with clear trade-offs called out (less analyzable, less optimizable, not the preferred path).

## Compile-time checks and diagnostics

This foundation layer must support diagnostics for:

- conflicting module ordering
- duplicate stage contributions where uniqueness is required
- unsatisfied variable requirements
- unsupported attempts to use internal re-query outside the approved module surface
- attributes or conventions used when the required module is not enabled
- duplicate host exposure or conflicting handler policies

## Observability and debuggability

The foundation layer should generate inspection artifacts for humans and AI agents, for example:

- operation -> stages -> contributors manifest
- applied module list per app
- generated service/variable usage summary
- host exposure summary

This should make it easy to answer "why did this operation get this generated code?" without reading raw generated C# first.

## Worked example for consumer extensibility

Use the Signature365 persistence pipeline as the reference example:

1. a package activates a persistence module and declares the services it needs (`IDocumentSession`, optional outbox services)
2. the transaction contributor produces a shared transaction-scope variable
3. the persistence contributor consumes `IDocumentSession` and emits the save-changes step
4. the outbox contributor consumes both the document session and the transaction scope
5. the debug manifest shows all three contributors and the variables they shared

If this example cannot be expressed cleanly, the foundation design is not ready.

## Explicit decisions

- Reuse `Blueprint.SourceGenerator.CodeWriter` for final rendering.
- Do not revive the full frame type hierarchy as the default authoring mechanism.
- Preserve ordered composition semantics, not legacy implementation details.
- Introduce a shared executor shell/runtime helper rather than regenerating the same logging/activity/exception scaffolding in every pipeline.
- General-purpose nested execution is not a foundation requirement. Any surviving re-query path must be explicit and narrow.
- Keep consumer extensibility a first-class goal from day one.

## Sequencing

This module must land before all others. Every other migration plan depends on it.

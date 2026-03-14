# Guiding principles

This repository should continue moving toward a compile-time-first, AOT-friendly model where the runtime stays simple, predictable, and observable.

## 1. Push work to code generation, not runtime

If the system can know something at build time, prefer generating it or validating it during generation instead of rediscovering it at runtime.

Current examples:

- `ApiResourceSourceGenerator` emits `*_ObjectProperty.g.cs` partials so resource metadata is synthesized at compile time instead of inferred later (`src\Blueprint.SourceGenerator\Generators\ApiResources\ApiResourceSourceGenerator.cs`).
- `IOperation` requires `static ApiOperationDescriptor Configure()` so operation metadata is shaped at compile time instead of being discovered dynamically (`src\Blueprint.Runtime.Model\IOperation.cs`).
- `OperationDescriptorSourceGenerator` emits descriptor partials and marker attributes (`src\Blueprint.SourceGenerator\Generators\Descriptors\OperationDescriptorSourceGenerator.cs`).
- `AppDataModelSourceGenerator` emits app registrations that call `this.Register(Operation.Configure())` (`src\Blueprint.SourceGenerator\Generators\App\AppDataModelSourceGenerator.cs`).
- `OperationExecutorPipelineSourceGenerator` emits executor pipelines and app executors instead of relying on reflection-driven dispatch (`src\Blueprint.SourceGenerator\Generators\Pipelines\OperationExecutorPipelineSourceGenerator.cs`).

Default question: can this be generated, precomputed, or diagnosed at build time? If yes, that is the preferred direction.

## 2. Prefer static analysis and diagnostics over runtime checks

Stringly-typed contracts should not stay stringly-typed if the compiler can validate them.

Current examples:

- `LinkGenerationModule` validates route placeholders against operation and resource members and reports `BPLG001` through `BPLG003` diagnostics (`src\Blueprint.SourceGenerator\Modules\LinkGenerationModule.cs`).
- Generator entry points fail early when required partial modifiers are missing (`BP001`, `BP002`, `BP005`, `BP104`) (`src\Blueprint.SourceGenerator\Generators\ApiResources\ApiResourceSourceGenerator.cs`, `src\Blueprint.SourceGenerator\Generators\App\AppDataModelSourceGenerator.cs`, `src\Blueprint.SourceGenerator\Generators\Descriptors\OperationDescriptorSourceGenerator.cs`, `src\Blueprint.SourceGenerator\Generators\Pipelines\DiagnosticDescriptors.cs`).
- Handler discovery and parameter resolution are analyzed during generation, not deferred to runtime (`src\Blueprint.SourceGenerator\Generators\Pipelines\ParameterAnalyzer.cs`).

Future work should follow the same pattern. For example, if a route template references a property, the build should prove that property exists and is valid for the intended usage.

## 3. Keep the runtime AOT-friendly, reflection-light, and allocation-aware

The runtime should be boring in the best possible way: pre-wired, low-surprise, and friendly to Native AOT and trimming.

Current signals:

- Generated executors register concrete operation types to concrete pipelines (`src\Blueprint.SourceGenerator\Generators\Pipelines\CodeGen\ExecutorCodeGenerator.cs`).
- `ApiOperationDescriptor` explicitly annotates `OperationType` with `DynamicallyAccessedMembers` so the trimmer preserves the required constructor (`src\Blueprint.Runtime.Model\ApiOperationDescriptor.cs`).
- Generated pipelines use `LoggerMessage.Define` and explicit activity creation instead of ad-hoc reflection-heavy logging (`src\Blueprint.SourceGenerator\Generators\Pipelines\CodeGen\PipelineCodeGenerator.cs`).

Do not add runtime reflection, runtime scanning, or lazy "we will figure it out later" fallbacks unless there is a strong reason and the AOT or trimming story is still explicit.

## 4. Treat serialization as a compile-time contract

Serialization should follow the same philosophy as routing and handler discovery.

This repository does not currently use `JsonSerializerContext`, but the intended direction is clear:

- Prefer `System.Text.Json` source generation over runtime metadata discovery.
- Where an API surface requires JSON serialization, prefer enforcing the presence and completeness of the right serializer context at compile time.
- If a feature depends on serialization metadata, make that dependency explicit and diagnosable instead of relying on runtime behavior.

The runtime should not be the first place we learn that a serializer contract is incomplete.

## 5. Build observability in, not on

Observability is part of the feature, not cleanup work for later.

Current signals:

- Generated pipelines create activities through `BlueprintActivitySource` and record exceptions on them (`src\Blueprint.Runtime\Diagnostics\BlueprintActivitySource.cs`, `src\Blueprint.SourceGenerator\Generators\Pipelines\CodeGen\PipelineCodeGenerator.cs`).
- The sample startup shows the intended OpenTelemetry direction, even though it is currently commented out (`src\Blueprint.SourceGenerator.Sample\Startup.cs`).

Future work should assume:

- full OpenTelemetry and OTLP compatibility is a goal
- important runtime paths should be traceable without reverse-engineering the code
- diagnostics, manifests, and inspection surfaces should help both developers and AI agents understand what the API is exposing

## 6. Design APIs, structures, and docs for human and AI consumers

The easiest system to maintain with AI help is one that is explicit, stable, and self-describing.

Prefer:

- stable naming and predictable generator output
- diagnostics with precise locations and concrete remediation guidance
- metadata that can be inspected without reading the entire codebase
- generated or registry-backed views of large API surfaces
- docs that explain why a pattern exists, not just what a type is called

If a large API surface would be hard for a human to inspect manually, it will also be hard for an AI agent. Add tooling, metadata, or inspection surfaces accordingly.

## 7. Make the repository self-correcting

Tool feedback and human feedback should improve the environment, not just the current patch.

That means:

- strengthen diagnostics when a bad configuration is easy to detect
- add or tighten tests when a mistake slips through
- update `/docs` when principles or architecture become clearer
- improve developer and AI tooling when inspection is too hard

The right response to repeated confusion is a better harness, not a reminder in a chat thread.

## Design filter for future changes

Before adding a new feature, ask:

1. Can the important parts be known and enforced at compile time?
2. Does this keep runtime behavior simple and AOT-friendly?
3. Are errors surfaced early, precisely, and in a machine-actionable way?
4. Is the result observable and inspectable enough for humans and AI agents?
5. If this goes wrong again, what test, diagnostic, or doc should exist so it gets corrected faster next time?

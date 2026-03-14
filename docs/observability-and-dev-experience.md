# Observability and developer experience

Observability is part of the product surface in this repository, not an optional runtime add-on.

The goal is not only "the code runs", but also "the generated and runtime behavior is inspectable enough that humans and AI agents can understand large API surfaces quickly".

## Current observability baseline

The repo already has real hooks that should be treated as foundations, not curiosities.

### Generated pipelines create structured traces and logs

`PipelineCodeGenerator` emits code that:

- starts an activity per pipeline execution
- logs through `LoggerMessage.Define`
- records exceptions onto activities

Key file:

- `src\Blueprint.SourceGenerator\Generators\Pipelines\CodeGen\PipelineCodeGenerator.cs`

Those choices matter because they keep logging and tracing explicit, low-overhead, and AOT-friendly.

### Blueprint has a shared activity source

`BlueprintActivitySource` centralizes the runtime activity source and exception tagging (`src\Blueprint.Runtime\Diagnostics\BlueprintActivitySource.cs`).

That gives the runtime a stable place to anchor tracing and makes it easier to integrate with OpenTelemetry.

### Legacy sample comments are not the current observability surface

`src\Blueprint.SourceGenerator.Sample\Startup.cs` contains commented historical snippets that mention `AddOpenTelemetryTracing(...)` and `AddBlueprintInstrumentation()`, but they sit inside an older commented sample block and should not be treated as the current integration surface.

Treat those comments as archival context, not as proof that a production-ready Blueprint OpenTelemetry API already exists. The current observability baseline is the generated pipeline tracing and the shared `BlueprintActivitySource`, and any future OpenTelemetry integration should be designed deliberately from the current source-generated architecture.

## Preferred direction

Future work should assume:

- OpenTelemetry is the primary observability model
- OTLP-compatible export should be a first-class outcome
- important runtime paths should be traceable without reverse-engineering the framework
- generated code should surface enough metadata to explain what was built

If a feature changes runtime behavior in a meaningful way, ask what traces, logs, metrics, metadata, or inspection surfaces should exist so the behavior can be understood quickly.

## Dev- and AI-friendly diagnostics

Diagnostics should not stop at "the build failed".

Prefer feedback that is:

- structured
- stable
- source-correlated
- specific enough to drive repair

For large or generated API surfaces, good developer experience means it should be easy to answer questions like:

- which operations were registered?
- which routes and links were generated?
- which handler pipeline will execute?
- which serializer context is expected?
- what diagnostics or warnings apply to this surface?

## Preferred inspection surfaces

Not all of these exist today, but they are good directions for future work:

- generated manifests or summaries of registered operations and links
- debug or dev-only endpoints that expose descriptor and route metadata safely
- CLI or tooling helpers that summarize large API surfaces
- machine-readable outputs that let AI agents inspect the generated shape without scraping logs

`BlueprintApp` already exposes registries of operations and links (`src\Blueprint.Runtime.Model\BlueprintApp.cs`). Treat those kinds of metadata structures as building blocks for richer inspection tooling.

## Observability checklist for new work

When adding or changing runtime behavior, ask:

- what trace span or event should exist?
- what structured log message should exist?
- what correlation data would help explain failures?
- what metadata or inspection surface would help on-call developers or AI agents understand the shape of the system?
- is the output compatible with the repo's AOT-friendly, low-reflection goals?

## Do not hide complexity

Large API surfaces become dangerous when the only way to understand them is to read source and mentally simulate multiple generators.

If a feature makes the surface larger or harder to inspect, it should also improve the diagnostics, manifests, tooling, or endpoints that explain that surface.

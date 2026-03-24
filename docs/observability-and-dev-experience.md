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

For generated machine-readable artifacts, extend `Blueprint.SourceGenerator.Artifacts.GeneratedManifestEnvelope` and keep the default `.blueprint/manifests/<artifact>.manifest.json` layout unless the migration plan explicitly changes that contract. Keep the checked-in schema artifact at `src\Blueprint.SourceGenerator\Artifacts\GeneratedManifestEnvelope.schema.json` aligned with the approved envelope so later CLI, endpoint, CI, and AI-facing projections share one deterministic machine-readable contract instead of fragmenting into ad-hoc schemas. The first cross-workstream reuse of that contract now lives in `src\Blueprint.SourceGenerator.Tests\Scale\`: the Tier 2 synthetic scale harness emits both a `scale-invalidation` envelope (`SyntheticFixtureInvalidationReport.cs`) and a `build-telemetry` envelope (`SyntheticBuildTelemetryReport.cs`). Together they let CI and AI review inspect scenario metadata, changed-output summaries, generated-output counts, tracked stage counts, and capture metadata without scraping imperative test assertions or ad-hoc logs. The first repo-native capture runner now lives at `migration\performance-and-scale\Capture-SyntheticBuildTelemetry.ps1`: it materializes a temporary synthetic app, emits cold-build, no-op-build, design-time-build, and single-edit-build `.binlog` plus `timing-summary.json` outputs, and writes a sibling `build-telemetry` envelope for each capture. The design-time capture intentionally records zero generated outputs because compiler execution is skipped while the runner still captures a comparable binlog and manifest summary. Keep runners like that honest by making emitted inspection files inspectable without letting later builds recompile them; the synthetic project removes `artifacts\generated\**\*.cs` from `Compile` so repeated captures measure the incremental graph rather than stale generated inputs. Phase 0 now also has a first non-microbenchmark concurrency proof for the runtime shell in `src\Blueprint.SourceGenerator.Tests.Integration\Generators\Pipelines\ConcurrentExecutorDispatchIntegrationTests.cs`: a start-gated harness fans 64 operations through the generated `IntegrationTestBlueprintAppExecutor.ExecuteAsync(...)` path, reports requested/completed/faulted counts plus returned payloads, and keeps the stress proof on the same executor seam that the dispatch benchmark measures. The next host-surface follow-on now lives in `src\Blueprint.SourceGenerator.Tests\Artifacts\ManifestInspectionEndpointStressTests.cs`: a repeated mapping harness rebuilds 40 development endpoint route builders, maps `MapBlueprintApi(...)`, and asserts stable endpoint counts plus hosted manifest-route hits on every iteration so startup stability is exercised on the same surface that `BlueprintEndpointMappingBenchmarks` measures. The current high-volume descriptor follow-on now sits beside those runtime checks in `src\Blueprint.SourceGenerator.Tests\Scale\SyntheticBlueprintFixtureScaleHarnessTests.cs`: the synthetic fixture can stamp unique root links across 100 operations, and a single root-link edit must change only `SyntheticOperation050_Descriptor.g.cs` so link-template pressure stays localized to descriptor output. Treat those deterministic harnesses as the `pull-request lane`: they are the always-on proof that should survive review and shared-runner variance. Keep noisier wall-clock, throughput, and higher-volume benchmark comparisons in a `scheduled/manual` lane so ordinary PRs are not gated on fragile shared-runner timing noise. The first preserved scheduled/manual review bundle now exists too: Phase 0 has a curated performance package that links the current benchmark markdown/HTML reports, a fresh synthetic telemetry capture run, and the reviewed manifest-backed scale snapshots into one human-review surface without inventing a second performance-only artifact shape. The next scale tier should follow the same pattern: publish Tier 3 readiness as a named review package with explicit scenarios, fresh scheduled/manual captures, and comparisons against the last approved performance bundle rather than relying on ad-hoc benchmark reruns.

The first activated contributor on that contract is the operations/discovery payload: `BlueprintSourceGenerationConfig.BuildDebugManifest(...)` plus `OperationDiscoveryManifestModule` and `OperationsDiscoveryManifestEmitter` now produce a deterministic `operations` manifest view with per-operation `kind`, `namespace`, and `typeName` summaries. `GeneratedManifestEnvelope.ToMarkdown()` is the first derived human-readable projection over that data, and it intentionally reads only envelope contents so tooling does not rediscover operation meaning separately. The underlying debug-manifest planning seam is deterministic across multiple contributors too: entry keys are contributor-owned, and `BuildDebugManifest(...)` now emits `BPMOD006` instead of silently allowing last-writer-wins replacement when two contributors try to reuse the same key for one operation. The first runtime inspection seam now complements that by loading manifest JSON from embedded assembly resources via `Blueprint.Runtime.Artifacts.ManifestResourceLoader` and roundtripping it through `GeneratedManifestEnvelope.FromJson(...)`, giving later runtime projections a deterministic source without calling contributor seams again. The current hosted follow-on keeps `Blueprint.Runtime` on the same boundary: `MapBlueprintApi(...)` can expose development-only `/.blueprint/manifests/operations.json` and `/.blueprint/manifests/operations.html` endpoints when `BlueprintHttpOptions.OperationsManifestResourceName` is configured. The JSON route still serves the canonical embedded manifest unchanged, and the HTML page is intentionally just a client-side projection over that JSON so the runtime does not grow a second manifest model. Development-only `/.blueprint/manifests/operations.md` and `/.blueprint/manifests/operations.schema.json` routes may now widen that hosted surface too, but only by serving prebuilt embedded bytes from `BlueprintHttpOptions.OperationsManifestMarkdownResourceName` and `BlueprintHttpOptions.OperationsManifestSchemaResourceName`; runtime code still must not call `ToMarkdown()` or invent schema-interpretation logic inside `Blueprint.Runtime`. Keep those hosted markdown fixtures semantically aligned with `GeneratedManifestEnvelope.FromJson(...).ToMarkdown()` even if line-ending or transport details vary by environment. Treat that pattern as the current preferred way to add richer hosted inspection without bypassing contributor-owned data or adding a `Blueprint.Runtime` reference back to `Blueprint.SourceGenerator`, but do not assume those exact fields are permanently frozen if later debug-manifest improvements need a better contributor-owned projection.

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

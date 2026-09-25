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

For generated machine-readable artifacts, extend `Blueprint.SourceGenerator.Artifacts.GeneratedManifestEnvelope` and keep the default `.blueprint/manifests/<artifact>.manifest.json` layout unless the migration plan explicitly changes that contract. Keep the checked-in schema artifact at `src\Blueprint.SourceGenerator\Artifacts\GeneratedManifestEnvelope.schema.json` aligned with the approved envelope so later CLI, endpoint, CI, and AI-facing projections share one deterministic machine-readable contract instead of fragmenting into ad-hoc schemas. The shared synthetic scenario seam now lives in `src\Blueprint.SourceGenerator.Benchmarks\Scale\SyntheticBlueprintFixtureGenerator.cs`, and both `Blueprint.SourceGenerator.Tests` and the runtime benchmark project consume it so compile-time locality proofs and runtime measurement baselines stop drifting onto unrelated synthetic naming schemes. On top of that shared seam, the Tier 2 synthetic scale harness emits both a `scale-invalidation` envelope (`SyntheticFixtureInvalidationReport.cs`) and a `build-telemetry` envelope (`SyntheticBuildTelemetryReport.cs`). Together they let CI and AI review inspect scenario metadata, changed-output summaries, generated-output counts, tracked stage counts, and capture metadata without scraping imperative test assertions or ad-hoc logs. The first repo-native capture runner now lives at `migration\performance-and-scale\Capture-SyntheticBuildTelemetry.ps1`: it materializes a temporary synthetic app, emits cold-build, no-op-build, design-time-build, and single-edit-build `.binlog` plus `timing-summary.json` outputs, and writes a sibling `build-telemetry` envelope for each capture. The design-time capture intentionally records zero generated outputs because compiler execution is skipped while the runner still captures a comparable binlog and manifest summary. Keep runners like that honest by making emitted inspection files inspectable without letting later builds recompile them; the synthetic project removes `artifacts\generated\**\*.cs` from `Compile` so repeated captures measure the incremental graph rather than stale generated inputs. Phase 0 now also has a first non-microbenchmark concurrency proof for the runtime shell in `src\Blueprint.SourceGenerator.Tests.Integration\Generators\Pipelines\ConcurrentExecutorDispatchIntegrationTests.cs`: a start-gated harness fans 64 operations through the generated `IntegrationTestBlueprintAppExecutor.ExecuteAsync(...)` path, reports requested/completed/faulted counts plus returned payloads, and can now also stress the benchmark executor shell by projecting non-`OkResult` outcomes through the same repeated dispatch harness. That keeps the repeated concurrency proof on the same executor seam and shared synthetic descriptor identities that the dispatch benchmark measures. The next host-surface follow-on now lives in `src\Blueprint.SourceGenerator.Tests\Artifacts\ManifestInspectionEndpointStressTests.cs`: a repeated mapping harness rebuilds 40 development endpoint route builders, maps `MapBlueprintApi(...)`, and asserts stable endpoint counts plus hosted manifest-route hits on every iteration so startup stability is exercised on the same surface that `BlueprintEndpointMappingBenchmarks` measures. The current high-volume descriptor follow-on now sits beside those runtime checks in `src\Blueprint.SourceGenerator.Tests\Scale\SyntheticBlueprintFixtureScaleHarnessTests.cs`: the synthetic fixture can stamp unique root links across 100 operations, and a single root-link edit must change only `SyntheticOperation050_Descriptor.g.cs` so link-template pressure stays localized to descriptor output. Treat those deterministic harnesses as the `pull-request lane`: they are the always-on proof that should survive review and shared-runner variance. Keep noisier wall-clock, throughput, and higher-volume benchmark comparisons in a `scheduled/manual` lane so ordinary PRs are not gated on fragile shared-runner timing noise. The first preserved scheduled/manual review bundle now exists too: Phase 0 has a curated performance package that links the current benchmark markdown/HTML reports, a fresh synthetic telemetry capture run, and the reviewed manifest-backed scale snapshots into one human-review surface without inventing a second performance-only artifact shape. The next scale tier should follow the same pattern: publish Tier 3 readiness as a named review package with explicit scenarios, fresh scheduled/manual captures, and comparisons against the last approved performance bundle rather than relying on ad-hoc benchmark reruns.

The first activated contributor on that contract is the operations/discovery payload: `BlueprintSourceGenerationConfig.BuildDebugManifest(...)` plus `OperationDiscoveryManifestModule` and `OperationsDiscoveryManifestEmitter` now produce a deterministic `operations` manifest view with per-operation `kind`, `namespace`, and `typeName` summaries plus ordered `hostExposure` arrays when compile-time host metadata exists. `GeneratedManifestEnvelope.ToMarkdown()` is the first derived human-readable projection over that data, and it intentionally reads only envelope contents so tooling does not rediscover operation meaning separately. The underlying debug-manifest planning seam is deterministic across multiple contributors too: entry keys are contributor-owned, `BuildDebugManifest(...)` now emits `BPMOD006` instead of silently allowing last-writer-wins replacement when two contributors try to reuse the same key for one operation, and `DebugManifestContributorContext.GetMetadata<T>()` lets manifest contributors reuse descriptor-planning metadata instead of reparsing operation attributes separately. The first runtime inspection seam now complements that by loading manifest JSON from embedded assembly resources via `Blueprint.Runtime.Artifacts.ManifestResourceLoader` and roundtripping it through `GeneratedManifestEnvelope.FromJson(...)`, giving later runtime projections a deterministic source without calling contributor seams again. The current hosted follow-on keeps `Blueprint.Runtime` on the same boundary: `MapBlueprintApi(...)` can expose development-only `/.blueprint/manifests/operations.json` and `/.blueprint/manifests/operations.html` endpoints when `BlueprintHttpOptions.OperationsManifestResourceName` is configured. The JSON route still serves the canonical embedded manifest unchanged, and the HTML page is intentionally just a client-side projection over that JSON so the runtime does not grow a second manifest model. Development-only `/.blueprint/manifests/operations.md` and `/.blueprint/manifests/operations.schema.json` routes may now widen that hosted surface too, but only by serving prebuilt embedded bytes from `BlueprintHttpOptions.OperationsManifestMarkdownResourceName` and `BlueprintHttpOptions.OperationsManifestSchemaResourceName`; runtime code still must not call `ToMarkdown()` or invent schema-interpretation logic inside `Blueprint.Runtime`. Keep those hosted markdown fixtures semantically aligned with `GeneratedManifestEnvelope.FromJson(...).ToMarkdown()` even if line-ending or transport details vary by environment. Treat that pattern as the current preferred way to add richer hosted inspection without bypassing contributor-owned data or adding a `Blueprint.Runtime` reference back to `Blueprint.SourceGenerator`, but do not assume those exact fields are permanently frozen if later debug-manifest improvements need a better contributor-owned projection.

## Observability checklist for new work

When adding or changing runtime behavior, ask:

- what trace span or event should exist?
- what structured log message should exist?
- what correlation data would help explain failures?
- what metadata or inspection surface would help on-call developers or AI agents understand the shape of the system?
- is the output compatible with the repo's AOT-friendly, low-reflection goals?

## HTTP error response surface

All error responses use [RFC 7807](https://www.rfc-editor.org/rfc/rfc7807) `application/problem+json` bodies, giving clients a consistent machine-readable error envelope across every failure path. The `HttpOperationResultExecutor` switch statement maps each `OperationResult` subclass to the appropriate HTTP response.

### Status codes and response shapes

| Status | Result type | Body | Detail source |
|--------|------------|------|---------------|
| 400 | `BindingFailureResult` | `ProblemDetails` | Binding failure reason (e.g. "Invalid route parameter 'id'") |
| 401 | `UnauthenticatedResult` | `ProblemDetails` | As legacy Blueprint wrote it: type `security_failure`; title the denying authoriser's `Message`, or "Access denied. Anonymous access is not allowed." for an anonymous caller; no detail |
| 403 | `ForbiddenResult` | `ProblemDetails` | From an authoriser, as legacy wrote it: type `unauthorized`, title the authoriser's `Message`, no detail (its `Reason` is logged, never written). From a resource authorization provider: fixed "You do not have permission to access this resource." |
| 415 | `UnsupportedMediaTypeResult` | `ProblemDetails` | Fixed: "The request content type is not supported. Expected 'application/json'." |
| 422 | `ValidationFailedResult` | `ValidationProblemDetails` | Per-member validation errors from DataAnnotations / IValidatableObject |
| 500 | `UnhandledExceptionResult` | `ProblemDetails` | Generic message unless `ExposeExceptionDetailsInErrorResponses` is true |
| 4xx | `UnhandledExceptionResult` (via `ApiException`) | `ProblemDetails` | Exception message (intentionally developer-authored) |

A 401, or an authoriser's 403, also carries a `traceId` extension (the operation activity's trace id) when the operation has
an activity, as legacy's did, and an authoriser's denial is logged at Information under `Blueprint.Authorisation.AuthorisationMiddlewareBuilder`
("Permission check failed. reason={0} authoriser={1}"). A denial whose failure type is not `Authorisation`, a default
`ExecutionAllowed`'s included, is a 401, as legacy made it.

### Exception detail control

`BlueprintHttpOptions.ExposeExceptionDetailsInErrorResponses` controls 500-level detail exposure:

- **false (default/production)**: 500 responses show `"An unexpected error occurred."` — no stack traces or internal details leak.
- **true (development)**: 500 responses include the exception message and stack trace in the `stackTrace` extension property.

Non-500 `ApiException` responses always include the exception message because `ApiException` is designed for developer-authored, client-facing error messages.

### OpenAPI alignment

The OpenAPI document generation declares `ProblemDetails` response schemas for all error status codes (400, 401, 403, 415, 422, 500), so client code generators produce strongly-typed error handling.

## Audit and sensitive-data observability surfaces

The auditing module contributes compile-time and runtime observability hooks:

### AuditingModule (pipeline contributor)

`src\Blueprint.SourceGenerator\Modules\Auditing\AuditingModule.cs` generates a PostExecution audit-sink call in every pipeline where auditing is not suppressed. Generated code:

- Checks `handlerResult.WasSuccessful` before auditing (only successful operations are audited)
- Resolves `IAuditSink` from DI at runtime (no-op if not registered)
- Respects `[DoNotAudit]` on the operation class to opt out entirely
- Best-effort: failures in the sink do not break the pipeline

Key files:

- `src\Blueprint.Runtime\Auditing\IAuditSink.cs`
- `src\Blueprint.Runtime\Auditing\DoNotAuditAttribute.cs`
- `src\Blueprint.SourceGenerator\Modules\Auditing\AuditingModule.cs`

### SensitivePropertyMetadataModule (descriptor contributor)

`src\Blueprint.SourceGenerator\Modules\Auditing\SensitivePropertyMetadataModule.cs` scans operation properties for `[Sensitive]` at compile time and emits `SetFeatureData<SensitivePropertyMetadata>(...)` in the generated descriptor constructor. This gives audit sinks and logging pipelines a reflection-free way to discover which properties need redaction.

Key files:

- `src\Blueprint.Runtime\Auditing\SensitiveAttribute.cs`
- `src\Blueprint.Runtime\Auditing\SensitivePropertyMetadata.cs`
- `src\Blueprint.SourceGenerator\Modules\Auditing\SensitivePropertyMetadataModule.cs`

### Pipeline applied-modules manifest

`PipelineCodeGenerator.AddGetAppliedModulesMethod(...)` generates a `GetAppliedModules()` static method on each pipeline class. It returns the ordered set of contributor IDs that participated in the plan for that operation, making it inspectable without runtime reflection or logs.

This supports:

- dev-time inspection of which modules are active per operation
- AI-agent-friendly introspection of generated pipeline composition
- compliance tooling that needs to verify module participation

### PerformanceMetricsModule (pipeline opt-out)

`src\Blueprint.SourceGenerator\Modules\Observability\PerformanceMetricsModule.cs` detects `[DoNotRecordPerformanceMetrics]` on operation types and sets `PipelinePlan.SuppressActivity` so the generated pipeline omits the `Activity`/trace span for that operation. This is useful for high-frequency, low-value operations (e.g., health checks, search endpoints) that would overwhelm trace collectors without providing diagnostic value.

Key files:

- `src\Blueprint.Runtime\Auditing\DoNotRecordPerformanceMetricsAttribute.cs`
- `src\Blueprint.SourceGenerator\Modules\Observability\PerformanceMetricsModule.cs`

### Structured audit events with trace correlation

The generated audit pipeline emits `System.Diagnostics.Activity` events with `blueprint.audit.*` semantic names:

- `blueprint.audit.started` — emitted before calling `IAuditSink` with a `blueprint.operation.type` tag
- `blueprint.audit.succeeded` — emitted after successful audit recording with a `blueprint.operation.type` tag
- `blueprint.audit.failed` — emitted on audit sink failure with `blueprint.operation.type`, `exception.type`, `exception.message`, and `exception.stacktrace` tags

Trace correlation is inherent: events are emitted on the enclosing `Activity`, so they carry the parent trace ID and span ID automatically. `IAuditSink` implementations can read `context.Activity?.TraceId` and `context.Activity?.SpanId` to persist alongside the audit record for cross-system correlation.

### Exception handling and HTTP status translation

The exception handling pipeline has two observable layers:

1. **`OperationExecutorShell`** (`src\Blueprint.Runtime\Exec\OperationExecutorShell.cs`) catches unhandled exceptions from the generated pipeline and wraps them in `UnhandledExceptionResult`. For `ApiException` subclasses with `HttpStatus < 500`, the shell logs at `Information` level (client errors) and does not set `ActivityStatusCode.Error` on the span. For server errors (>= 500) or non-`ApiException` exceptions, it logs at `Error` level, records the exception on the activity, and sets the span status to `Error`.

2. **`HttpOperationResultExecutor`** (`src\Blueprint.Runtime\Http\HttpOperationResultExecutor.cs`) translates `UnhandledExceptionResult` to an HTTP response. It checks whether the underlying exception is `ApiException` and respects its `HttpStatus`. Client errors (< 500) expose the exception message in the problem detail response; server errors (>= 500) hide the message unless `BlueprintHttpOptions.ExposeExceptionDetailsInErrorResponses` is true.

This two-layer design means trace spans naturally carry the exception info (with correct severity) while the HTTP response controls what the client sees. OTel spans show the full picture; client responses show only safe details.

### Messaging outbox coordination

The `MessageOutboxModule` contributes a PostExecution render step at Order 920 (after UnitOfWork save at 900, before auditing at 950) that commits enqueued messages via `IMessageOutbox.CommitAsync`. `IMessageOutbox` is Blueprint's own seam, with no legacy equivalent; legacy Blueprint's `StartRebusTransactionScopeMiddleware` / `CommitRebusTransactionScopeMiddleware` pair is replaced by the `RebusTransactionModule`, and its Marten session outbox by a consumer's `IRebusTransactionEnlistment`.

Key characteristics:
- **Commands and background tasks**: only command and background task operations participate (queries never have outbox commits); background tasks joined in v1.0.3
- **Success-guarded**: `handlerResult.WasSuccessful` must be true for commit
- **Opt-out**: `[DoNotPublishMessages]` suppresses outbox commit per-operation
- **Optional resolution**: if no `IMessageOutbox` is registered, the step is a no-op
- **Diagnostic**: `BPM001` warning for `[DoNotPublishMessages]` on an operation that is neither a command nor a background task

The `MessageOutboxManifestModule` contributes `messaging.outboxCommitEnabled` to the debug manifest.

Key files:
- `src\Blueprint.Runtime.Model\Messaging\IMessageOutbox.cs`
- `src\Blueprint.Runtime.Model\Messaging\DoNotPublishMessagesAttribute.cs`
- `src\Blueprint.SourceGenerator\Modules\Messaging\MessageOutboxModule.cs`
- `src\Blueprint.SourceGenerator\Modules\Messaging\MessageOutboxManifestModule.cs`

## Do not hide complexity

Large API surfaces become dangerous when the only way to understand them is to read source and mentally simulate multiple generators.

If a feature makes the surface larger or harder to inspect, it should also improve the diagnostics, manifests, tooling, or endpoints that explain that surface.

## Performance benchmarks

The `Blueprint.SourceGenerator.Benchmarks` project provides micro-benchmarks for hot-path operations. All benchmarks use `InProcessEmitToolchain` (the source generator prevents BDN's out-of-process build from succeeding).

Run benchmarks with:

```bash
dotnet run --project src/Blueprint.SourceGenerator.Benchmarks -c Release -- --filter "*BenchmarkName*"
```

### Executor dispatch (per-request hot path)

| Scenario | Operations | Mean | Allocated |
|---|---|---|---|
| Pipeline lookup | 10–500 | ~13 ns | 0 B |
| Direct pipeline execution | 10–500 | ~52–67 ns | 216 B |
| Full executor shell dispatch | 10–500 | ~65–89 ns | 216 B |

Pipeline lookup is O(1) with zero allocations regardless of registered operation count. The executor shell adds ~20 ns overhead over direct pipeline execution.

### Validation (per-request)

| Scenario | Mean | Allocated |
|---|---|---|
| Fast-path (compile-time generated) | 2.3–4.5 μs | 2.0–3.1 KB |
| Runtime fallback (DataAnnotations) | 4.4–7.4 μs | 3.0–4.0 KB |

Compile-time validation fast paths are 40–60 % faster than the DataAnnotations runtime fallback and allocate less memory.

### App registration (startup)

| Operations | Mean | Allocated |
|---|---|---|
| 10 | ~2 μs | ~4 KB |
| 100 | ~25 μs | ~45 KB |
| 500 | ~103 μs | ~219 KB |

Registration scales linearly with operation count.

### Property population (per-request)

| Scenario | Mean | Allocated |
|---|---|---|
| Route-only (3 props) | ~4.3 μs | ~2.5 KB |
| Mixed route + query (6 props) | ~6.7 μs | ~3.8 KB |
| Body-only (JSON) | ~7.6 μs | ~4.2 KB |
| Typed properties (enums, guids) | ~8.9 μs | ~5.0 KB |
| Large payload (10 props) | ~10.0 μs | ~6.5 KB |

### Endpoint mapping (startup)

| Operations | Mean | Allocated |
|---|---|---|
| 10 | ~82 μs | ~56 KB |
| 100 | ~628 μs | ~489 KB |
| 250 | ~1.5 ms | ~1.2 MB |
| 500 | ~3.4 ms | ~2.4 MB |

Endpoint mapping scales linearly. For a typical Signature365-sized API (~100 operations), startup mapping completes in under 1 ms.

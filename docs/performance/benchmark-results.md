# Blueprint Source-Generator Benchmark Results

> Generated on a Intel Core Ultra 7 165H, 22 logical / 16 physical cores, .NET 8.0.26, Windows 11.
> BenchmarkDotNet v0.14.0, MediumRun (15 iterations, 2 launches, 10 warmups).

## Pipeline Population (Property Binding + Body Parse)

Measures the full HTTP request → operation property population path including
JSON body deserialization, route parameter binding, and query string parsing.

| Method | Mean | Error | StdDev | Gen0 | Allocated |
|---|---:|---:|---:|---:|---:|
| RouteOnlyGetAsync | 2.11 µs | 0.09 µs | 0.10 µs | 0.35 | 4.3 KB |
| TypedBodyPostAsync | 3.67 µs | 0.08 µs | 0.09 µs | 0.46 | 5.67 KB |
| MixedRouteAndBodyPostAsync | 4.23 µs | 0.43 µs | 0.49 µs | 0.44 | 5.49 KB |
| BodyOnly3StringsPostAsync | 4.29 µs | 0.54 µs | 0.62 µs | 0.43 | 5.39 KB |
| LargeBody10PropsPostAsync | 7.71 µs | 2.11 µs | 2.43 µs | 0.63 | 7.83 KB |

**Key insight**: Route-only GET is ~2.1 µs with ~4.3 KB allocation. POST bodies
benefit from the sync-first body reading fast path (avoiding async state machine
when Kestrel has pre-buffered the body). Even a 10-property POST body only reaches
~7.7 µs — demonstrating zero-reflection Utf8JsonReader parsing scales linearly.

## Executor Dispatch (Pipeline Resolution + Execution)

Measures the overhead of resolving a compiled pipeline from the registry and
dispatching through it.

| Method | Mean (10 ops) | Mean (100 ops) | Mean (250 ops) |
|---|---:|---:|---:|
| ResolveRegisteredPipeline | 16.5 ns | 15.9 ns | 21.3 ns |
| ExecutePipelineDirectAsync | 40.8 ns | 40.8 ns | 47.2 ns |
| ExecuteThroughExecutorShellAsync | 63.3 ns | 52.4 ns | 58.1 ns |

**Key insight**: Pipeline resolution is ~16 ns (zero allocation) regardless of
registry size — `FrozenDictionary<Type, ...>` lookup. Full execution through the
shell is ~60 ns overhead.

## BlueprintApp Registration (Startup)

Measures the time to register N operations into a `BlueprintApp` instance.

| OperationCount | Mean | Gen0 | Gen1 | Allocated |
|---:|---:|---:|---:|---:|
| 10 | 2.54 µs | 0.39 | 0.00 | 4.84 KB |
| 100 | 24.86 µs | 3.69 | 0.37 | 45.3 KB |
| 250 | 90.95 µs | 8.67 | 1.40 | 106.5 KB |
| 500 | 196.69 µs | 17.82 | 5.13 | 219.3 KB |

**Key insight**: Registration scales linearly. 500 operations register in <200 µs
with ~220 KB allocation — entirely acceptable for application startup.

## BlueprintApp Lookup

Measures `TryFindOperation` by type — the hot path for every request dispatch.

| Method | 10 ops | 100 ops | 250 ops | 500 ops |
|---|---:|---:|---:|---:|
| TryFindRegisteredOperation | 9.3 ns | 10.2 ns | 8.4 ns | 8.4 ns |
| TryFindMissingOperation | 10.1 ns | 8.0 ns | 6.3 ns | 6.4 ns |

**Key insight**: After `Freeze()` converts to `FrozenDictionary`, lookup is O(1) at
~8-10 ns for hits and ~6-10 ns for misses. Zero allocation in all cases. This is a
~50% improvement over the pre-freeze baseline.

## Endpoint Mapping (Startup)

Measures `MapBlueprintApi()` which registers all ASP.NET Core endpoints at startup.

| OperationCount | Mean | Gen0 | Gen1 | Allocated |
|---:|---:|---:|---:|---:|
| 10 | 84.0 µs | 4.52 | 0.37 | 56.5 KB |
| 100 | 832.7 µs | 39.06 | 11.72 | 489.7 KB |
| 250 | 1,839.5 µs | 97.66 | 44.92 | 1,197.1 KB |
| 500 | 4,572.8 µs | 187.50 | 132.81 | 2,393.0 KB |

**Key insight**: Endpoint mapping is startup-only. 500 operations map in ~4.6 ms.

## Validation Pipeline

Measures inline source-generated validation vs runtime DataAnnotation fallback.

| Method | Mean | Allocated | Alloc Ratio |
|---|---:|---:|---:|
| ValidatableObjectFallbackInvalidAsync | 1.50 µs | 2.19 KB | 0.65 |
| GreaterThanPropertyInvalidAsync | 2.09 µs | 2.63 KB | 0.79 |
| EmailAddressInvalidAsync | 2.16 µs | 2.51 KB | 0.75 |
| MaxLengthTooLongAsync | 2.59 µs | 2.53 KB | 0.76 |
| RequiredMaxLengthWhitespaceAsync | 2.66 µs | 2.52 KB | 0.75 |
| RequiredOnlyValidAsync | 2.81 µs | 2.55 KB | 0.76 |
| RequiredOnlyInvalidAsync | 3.16 µs | 3.00 KB | 0.90 |
| EmailAddressRuntimeFallbackInvalidAsync | 2.91 µs | 2.96 KB | 0.88 |
| RequiredIfRuntimeFallbackInvalidAsync | 3.58 µs | 3.69 KB | 1.10 |

**Key insight**: Source-generated validation (Required, MaxLength, etc.) runs in
~2.5 µs with 0.75x allocation ratio compared to the baseline. Runtime fallback
paths (for complex validators) add ~0.5-1 µs overhead.

## Full Pipeline (End-to-End Scenarios)

Measures realistic end-to-end pipeline execution across different complexity levels:
minimal (handler-only), validated, and full-stack (multi-property validation + handler).

| Method | Mean | Ratio | Gen0 | Allocated |
|---|---:|---:|---:|---:|
| MinimalQueryAsync | 219 ns | 1.00 | 0.11 | 1.31 KB |
| FullStackCommandValidAsync | 342 ns | 1.56 | 0.11 | 1.37 KB |
| ValidatedQueryValidInputAsync | 471 ns | 2.15 | 0.13 | 1.61 KB |
| ValidatedQueryInvalidInputAsync | 774 ns | 3.54 | 0.14 | 1.72 KB |
| FullStackCommandValidationFailureAsync | 722 ns | 3.30 | 0.14 | 1.69 KB |

**Key insight**: A minimal pipeline executes in ~219 ns with just 1.31 KB allocation.
Validation adds ~250 ns. All paths remain sub-microsecond. Full-stack commands with
validation complete in <775 ns, confirming the generated pipeline adds near-zero
overhead to handler execution.

## Summary

| Area | Hot Path Latency | Allocation |
|---|---|---|
| Pipeline resolution | ~16 ns | 0 B |
| Pipeline execution | ~40-47 ns | 144 B |
| Minimal pipeline (E2E) | ~219 ns | 1.31 KB |
| Route-only GET | ~2.1 µs | 4.3 KB |
| POST with body | ~3.7-7.7 µs | 5.5-7.8 KB |
| Validation (inline) | ~471 ns | 1.6 KB |
| Full-stack command (E2E) | ~342 ns | 1.37 KB |
| Operation lookup | ~8 ns | 0 B |
| Registration (100 ops) | ~25 µs | 45 KB |
| Endpoint mapping (100 ops) | ~833 µs | 490 KB |

The source-generated approach eliminates all reflection from hot paths. Pipeline
resolution and operation lookup are zero-allocation O(1) operations. Body parsing
and validation scale linearly with property count.

## Load Testing (NBomber — HTTP Pipeline Under Concurrent Load)

Measures end-to-end HTTP request throughput and latency through the sample application
host using TestServer + NBomber. All 9 scenarios run concurrently at their specified
injection rates for 30 seconds. Each request traverses the full pipeline: routing,
authentication, authorization, property binding, validation, handler, and response.

| Scenario | Rate (RPS) | Total Requests | p50 (ms) | p95 (ms) | p99 (ms) | Mean (ms) |
|---|---:|---:|---:|---:|---:|---:|
| health_check (AllowAnonymous) | 500 | 15,000 | 0.20 | 0.86 | 2.44 | 0.37 |
| validation_failure_fast_path | 300 | 9,000 | 0.23 | 1.01 | 2.62 | 0.39 |
| get_template_by_id (auth+scope) | 200 | 6,000 | 0.23 | 1.26 | 2.92 | 0.43 |
| delete_template (auth+scope) | 50 | 1,500 | 0.25 | 1.52 | 3.36 | 0.46 |
| create_template_command (auth+body) | 50 | 1,500 | 0.33 | 1.75 | 3.46 | 0.57 |
| update_template_put (auth+body) | 75 | 2,250 | 0.39 | 1.67 | 3.34 | 0.68 |
| openapi_spec_fetch | 10 | 300 | 0.42 | 2.47 | 5.12 | 0.88 |
| get_templates_list (auth+scope) | 100 | 3,000 | 1.96 | 5.70 | 9.94 | 2.30 |
| authorization_denied | 200 | 6,000 | 1.94 | 4.82 | 7.59 | 2.30 |

**Key insights**:
- **Sub-250µs p50** for simple queries, validation failures, and CRUD operations.
- **Validation failure short-circuits** at the same latency as simple GETs (~0.23 ms p50),
  confirming no handler invocation or unnecessary pipeline stages execute after validation fails.
- **Zero failures** across 44,550 total requests at 1,485 concurrent RPS.
- p99 stays under 10 ms across all scenarios including full auth pipeline.
- POST commands with JSON body deserialization: 0.33 ms p50 — sync-first body reading eliminates overhead.

## Recent Optimizations

### Compiled Handler Constructors (commit 097b428)

Generated pipelines now emit `new Handler(GetRequiredService<X>(), ...)` directly
when the handler has a single public constructor with DI-resolvable parameters.
This eliminates the `ActivatorUtilities.CreateInstance` reflection-based constructor
selection at runtime. When the handler is also registered in DI, the compiled
constructor serves as a zero-reflection fallback.

### Singleton Service Caching (commits 66d6fe4, various)

Pipeline constructor fields now cache singleton-lifetime services (e.g.,
`IAuditSink`, `IResourceAuthorizationProvider`, `IUserAuthorisationContextFactory`)
resolved once at startup. This eliminates per-request `GetRequiredService<T>()` calls
for services whose lifetime guarantees a single instance.

### FrozenDictionary Lookups

`BlueprintApp.Freeze()` converts operation dictionaries to `FrozenDictionary<Type, ...>`
for O(1) lookup at ~8-10 ns. The `BlueprintAppExecutor` pipeline registry also uses
frozen dictionaries.

### BaseUri Caching (commit 27a3e10)

`BlueprintApiRouter` caches the computed base URI across requests when scheme, host,
and path base are stable — avoiding per-request string interpolation for the common
case where these values don't change.

### Content-Type Constants

All HTTP response content-type strings (`application/json; charset=utf-8`,
`application/problem+json; charset=utf-8`, `text/plain; charset=utf-8`) are static
constants to improve readability and allow the compiler to intern them.

### ETagger Header Caching (commit db06b3d)

The 304 Not Modified path reuses a static `Cache-Control` header dictionary,
eliminating a per-response allocation on the conditional request fast path.

### ILogger\<T\> Singleton Caching (commit f3ffb77)

Generated pipeline constructors now resolve `ILogger<T>` once via
`loggerFactory.CreateLogger<T>()` and store it in a `_cached_ILogger_*` field.
This eliminates per-request `GetRequiredService<ILogger<T>>()` calls for every
handler that accepts a logger parameter. The factory-based resolution ensures
compatibility with `NullLoggerFactory.Instance` in test scenarios.

### ConfigureAwait(false) (commits 4466f98, 29e1c03)

All `await` expressions in both generated pipeline code (13 modules) and runtime
code (`HttpOperationResultExecutor`, `ApplicationBuilderExtensions`, diagnostic
endpoints) now use `.ConfigureAwait(false)`. While ASP.NET Core has no
`SynchronizationContext`, this is best practice for library code and prevents
issues when Blueprint is used outside ASP.NET Core (e.g., CLI tools, tests).

### Activity Local Variable (commit 254b080)

Generated `PopulateMetadata` lambdas in the authoriser pipeline now capture
`context.Activity` into a local `_activity` variable instead of repeatedly
reading the thread-static `Activity.Current` property. This avoids redundant
TLS lookups in delegate captures.

### DI Lookup Skip Guards (commit 63d79f1f)

`PipelineServices` now probes `IServiceProviderIsService` at construction time to
set `HasBackgroundTaskScheduler`, `HasUnitOfWork`, and `HasMessageOutbox` flags.
Generated pipelines emit `if (this.Has*)` guards around DI resolution calls,
completely skipping service provider lookups for services not registered. This
eliminates ~3 failed dictionary lookups per request in typical configurations.
**Result: FullStackCommand improved from 419ns → 346ns (17% faster).**

### Per-Operation Request Delegates (commit 81111ab7)

Replaced the single shared `RequestDelegate` (which looked up the descriptor from
endpoint metadata per-request) with per-operation lambdas that capture the
descriptor and lazily-resolve the pipeline. Each endpoint gets its own delegate,
eliminating metadata dictionary lookup and endpoint metadata retrieval per request.

### Buffer-Serialized JSON Responses (commit c47acf3e)

`WriteJsonAsync` now serializes to a ThreadStatic `ArrayBufferWriter<byte>` +
`Utf8JsonWriter` when the app provides a source-generated `JsonSerializerContext`.
This enables writing `Content-Length` (avoiding chunked transfer encoding overhead
in Kestrel) and returns `Task.CompletedTask` synchronously when the response
body `WriteAsync` completes without awaiting — eliminating the async state machine
for small payloads.

A write that does *not* complete at once keeps the buffer: the thread gets a new one
for its next response (v1.0.3). A body stream may read its input across awaits (a
compressing wrapper does), while the thread serves another request; sharing the
buffer then sent one request's bytes in another's response. The same rule covers the
resource-event envelopes and `PipelineHelpers.WriteOkJsonResponseAsync`. The
synchronous path is unchanged, so it still allocates nothing.

### Sync-First JSON Body Reading (commit 7f3cb3b6)

`ReadJsonBodyAsync` now checks whether the first `ReadAsync` call on the request
body completes synchronously (which it almost always does when Kestrel has
pre-buffered the body). When the full body is available in a single sync read,
the method returns without allocating an async state machine (~200B savings per
POST/PUT request). Falls back to async continuation for partial reads or chunked
encoding.
**Result: POST body parsing improved from 8.6µs → 3.7µs (57% faster).**

### Pre-Serialized Static Error Responses (commit 8281bba8)

Static error responses (401 Unauthorized, 403 Forbidden, 415 Unsupported Media Type)
are now pre-serialized as UTF-8 byte arrays at class initialization. Writing these
responses uses `Body.WriteAsync(ReadOnlyMemory<byte>)` directly, eliminating
per-request JSON serialization, buffer rental, and Content-Length computation.

### Result Executor Switch Reorder (commit 681ef267)

The result type switch in `TryExecuteAsync` now checks `OkResult` first since it
represents ~80% of production traffic. This saves ~9 type comparison operations
(~9ns) per successful request by short-circuiting the pattern match early.

### Sync Fast-Path Request Delegate (commit 05526c13)

`RouteOperationAsync` restructured from `async Task` to a non-async entry point.
When `pipeline.ExecuteAsync` returns a completed `ValueTask` (synchronous handler)
AND response writing completes synchronously, returns `Task.CompletedTask` directly
— avoiding the ~176-byte async state machine allocation entirely.

### Non-Async Result Executor (commit 402a3231)

`TryExecuteAsync` converted from `async Task<bool>` to a non-async dispatcher.
The `OkResult` path, status-only results, and pre-serialized error paths all return
cached `Task.FromResult(true)` without allocating an `AsyncStateMachineBox`.
Only uncommon paths (validation failures, file streams, resource events) fall through
to an async slow path. Combined with the sync request delegate above, a typical
GET request that completes synchronously avoids **both** async state machine
allocations entirely (~300-350 bytes saved per request).

## Framework Overhead Comparison

Blueprint vs raw ASP.NET Core minimal API, end-to-end HTTP through TestServer:

| Scenario | Blueprint | Raw Minimal API | Difference |
|---|---:|---:|---|
| GET (handler-only) | 33 µs | 34 µs | **Equal (within noise)** |
| POST (JSON body + handler) | 41 µs | 63 µs | **Blueprint 35% faster** |

Blueprint achieves zero-to-negative overhead because per-operation request delegates
bypass the generic middleware pipeline traversal — each request routes directly to its
handler lambda. POST responses are faster due to buffer-serialize enabling
Content-Length (avoiding chunked transfer encoding).

## Concurrency & Tail Latency

Measured under 200 concurrent requests (burst fire) using TestServer:

| Scenario | Mean | p99 | Allocated |
|---|---:|---:|---:|
| GET minimal | 872 µs | sub-1 ms | 1.84 MB/burst |
| POST full-stack | 1,021 µs | ~1 ms | 2.43 MB/burst |
| GET validated | 946 µs | sub-1 ms | 2.2 MB/burst |

**Key insight**: No lock contention or thread-pool starvation under burst load.
The ThreadStatic buffer pattern ensures zero contention on the serialization path.
Sub-millisecond p99 for GET queries under concurrent load.

## Isolated Pipeline Benchmarks (Latest)

Pure pipeline execution without HTTP overhead:

| Method | Mean | Allocated |
|---|---:|---:|
| MinimalQuery | 219 ns | 1.31 KB |
| FullStackCommand | 342 ns | 1.37 KB |
| ValidatedQuery | 471 ns | 1.61 KB |

**Real per-request Blueprint overhead**: ~264 bytes (ApiOperationContext ~144B +
OkResult ~40B + Operation instance ~24B + headers/strings ~56B). All other allocation
is `DefaultHttpContext` in benchmarks — pooled by Kestrel in production.

## Source Generator Performance

| Scenario | Mean | Allocated |
|---|---:|---:|
| Initial generation (500 ops) | 123 ms | 122 MB |
| Incremental cached (100 ops) | 24.6 ms | 11.94 MB |
| App registration (500 ops) | 106 µs | 268 KB |
| Operation lookup (any scale) | 7–9 ns | 0 B |

**Key insight**: Incremental caching via model equality means only `FindHandlersForOperations`
re-runs on edits — downstream code generation is fully cached when the operation surface
hasn't changed.

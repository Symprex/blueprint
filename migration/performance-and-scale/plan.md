# Performance and scale

- Status: Planned
- Priority: P0
- Classification: Cross-cutting engineering system
- Depends on: no hard dependency for the first scale-harness milestone; then evolves alongside `../foundation-module-system/plan.md`, `../operation-discovery-and-descriptors/plan.md`, and `../pipeline-composition-and-execution/plan.md`

## Why this module exists

The source-generated rewrite only succeeds if it scales at Signature365 size.

That means more than "the generators work on a small sample". It means:

- compile-time analysis stays incremental as operation and project counts grow
- IDE and build feedback stay localized when a single operation changes
- startup, endpoint mapping, and executor dispatch stay bounded at runtime
- regressions are caught by deterministic harnesses instead of anecdotal complaints after a large migration has already landed

Without an explicit scale plan, we risk building a compile-time-first architecture that is correct but too expensive to edit, build, and run in real Symprex solutions.

## Evidence

- `src\Blueprint.SourceGenerator.Tests\TestHelpers.cs` already treats cacheability as a first-class invariant: `Compile<T>()` compiles generated output, reruns the incremental generator, and asserts cached second-run outputs.
- `src\Blueprint.SourceGenerator.Tests\Generators\Pipelines\OperationExecutorPipelineGeneratorIntegrationTests.cs` currently disables cacheability assertions for the pipeline integration helper, showing that the existing harness is useful but not yet the full answer for larger or more complex scenarios.
- `src\Blueprint.Runtime.Model\BlueprintApp.cs` currently performs duplicate URL checks by scanning previously-registered descriptors and URLs during registration.
- `src\Blueprint.Runtime\Http\ApplicationBuilderExtensions.cs` maps endpoints by iterating the full registered operation and URL set at startup.

Those are all reasonable current shapes for a small rewrite, but they are also the kinds of places that need explicit measurement before we claim Signature365-scale readiness.

## Migration goal

Create a performance and scale harness that protects:

1. incremental-generator cacheability and invalidation locality
2. build, IDE, and analyzer responsiveness for multi-project Blueprint solutions
3. runtime startup, dispatch, and result-path allocation behavior
4. regression visibility through machine-readable artifacts and stable benchmark scenarios

The goal is not to overfit to one machine or one CI runner. The goal is to make regressions visible, explainable, and reviewable.

## Scale tiers to target

Use explicit scale tiers so discussions about performance stop being vague.

### Tier 1 - Reference-app scale

- roughly 10-20 operations
- a few resource types and route groups
- human-readable and documentation-friendly

This tier is covered primarily by the reference app plan and ordinary integration tests.

### Tier 2 - Medium CI scale

- roughly 100-250 operations
- several projects to mimic a real contract/host split plus companion packages
- representative edits such as adding one operation, changing one handler, or changing one route template

This tier should be exercised in normal CI through deterministic tests and selected benchmark/smoke runs.

### Tier 3 - Signature365 stress scale

- roughly 500+ operations across many projects
- grouped handlers, auth metadata, links, validation, and result metadata all present
- cold build, warm build, no-op rebuild, and single-edit rebuild scenarios

This tier should be exercised through scheduled or explicitly-invoked benchmark/stress workflows rather than every pull request.

## Harnesses to build

### 1. Deterministic compile-time scale tests

Add test fixtures that generate synthetic Blueprint apps and operation sets at controlled sizes.

The compile-time harness should assert:

- generation succeeds and diagnostics match expectations
- second runs are fully cached when the inputs are unchanged
- combined multi-generator runs stay localized when descriptor, app, and pipeline generators all participate in the same compilation
- one-operation edits only invalidate the expected descriptors, registrations, and pipelines
- shared-contract edits invalidate the expected downstream surfaces and no more
- generated file count and generated code volume grow in a predictable, approximately linear way rather than exploding combinatorially
- tracked stages such as `InitialExtraction`, `Configuration`, `CombineWithCompilation`, and `FindHandlers` remain explainable under scale
- analyzer diagnostics and analyzer-execution cost remain bounded once migration analyzers and other compile-time checks exist

This is the most important IDE/compiler protection because it tests locality, not just elapsed time.

### 2. Build, IDE, and analyzer telemetry harness

The repository needs a large-solution harness that can answer:

- how much work does a cold build do?
- what changes on a no-op rebuild?
- what changes when one operation or one shared contract changes?
- which generator/analyzer stages dominate the build?
- what changes between a normal build and a design-time build?
- how much memory does the compiler/generator/analyzer process consume at higher tiers?

Planned outputs:

- binary logs (`.binlog`)
- machine-readable summaries of generator/analyzer timings
- design-time-build summaries
- generated-file counts and aggregate generated LOC
- invalidation summaries by operation/project
- peak working-set or equivalent memory telemetry for larger scenarios

The preferred proxy for IDE health is not "time one Visual Studio click". It is localized invalidation plus design-time/build-time telemetry from the same underlying compile graph.

### 3. Runtime benchmark harness

Add dedicated runtime benchmarks for the paths that should stay cheap:

- endpoint mapping/startup for Blueprint HTTP hosts
- generated executor dispatch
- shared executor shell overhead
- happy-path semantic result normalization/execution
- validation and authorisation once those modules land
- representative error and exception translation paths

The primary measurements are:

- allocations on the happy path
- steady-state latency/throughput
- startup/registration cost as operation count grows

### 4. Stress and soak tests

Add non-microbenchmark stress tests for:

- concurrent executor dispatch
- repeated host startup and endpoint mapping
- large manifest/OpenAPI generation once those modules exist
- high-volume parameter-binding and link-generation scenarios where string/template handling could become unexpectedly expensive

These do not replace BenchmarkDotNet-style measurements; they complement them by proving the runtime remains stable under sustained or concurrent use.

### 5. Shared synthetic fixture generation

Do not hand-author hundreds of stress operations.

Plan for a deterministic fixture generator that can emit:

- contract-only operation/resource sets
- multi-project app layouts
- controlled mixes of auth, validation, links, and result shapes
- targeted mutation scenarios for invalidation testing

That generator should feed both the compile-time and runtime harnesses so they measure the same architectural shapes.

Treat this fixture generator as an explicit first-milestone deliverable, not a later convenience.

Minimum expectations:

- deterministic inputs for operation count, feature mix, and project topology
- a repeatable mutation model for "single operation changed", "shared contract changed", and similar invalidation scenarios
- outputs that can feed both compile-time tests and runtime benchmark surfaces without hand-maintained large samples

## First milestone definition

The first performance milestone should be concrete enough that future implementation work can start without reopening planning.

Minimum deliverables:

- a deterministic synthetic fixture generator
- medium-scale compile-time cacheability and invalidation-locality tests
- a documented way to capture design-time-build and normal-build telemetry
- a first benchmark or measurement surface for the known runtime registration hotspots

Do not block the first milestone on a complete benchmark suite, CI history, or every future runtime path being measurable on day one.

## Known hotspots to redesign before Tier 3

Not every scale issue should wait for a benchmark to tell us it exists.

The current runtime model already has a few obvious algorithmic hotspots that should be redesigned before we treat Tier 3 as credible:

- `src\Blueprint.Runtime.Model\BlueprintApp.cs` does duplicate-URL checks by scanning previously-registered descriptors and URLs during `Register(...)`
- `src\Blueprint.Runtime.Model\BlueprintApp.cs` uses linear descriptor searches in `TryFindOperation(...)`

The performance plan should therefore treat these as both benchmark targets and expected design cleanups, not just as measurements to observe passively.

## Reference app versus scale fixtures

Do not overload the human-facing example with scale-fixture responsibilities.

The reference app should stay curated and readable. Large-scale performance harnesses should be driven by synthetic fixtures that deliberately stress project and operation counts.

The two surfaces should share patterns and contracts, but not the same size target.

## CI and execution model

Split the performance story into two lanes.

### Pull-request lane

Must stay deterministic and review-friendly:

- ordinary unit, snapshot, and integration tests
- medium-scale cacheability and invalidation-locality tests
- compile-time smoke checks for synthetic scale fixtures
- selected runtime smoke or allocation checks where the signal is stable

Hard gates belong here when they are deterministic:

- cacheability failures
- unexpectedly broad invalidation
- output-shape explosions
- clearly-regressed allocation contracts on known zero-allocation paths

### Scheduled or manually-invoked lane

Use this lane for noisier but valuable measurements:

- larger synthetic solutions
- cold/warm/no-op build timing runs
- runtime latency and throughput benchmarks
- startup and endpoint-mapping measurements
- benchmark artifact publishing and historical comparisons

Do not gate everyday pull requests on fragile wall-clock budgets from shared runners.

## Artifact model

Every scale or benchmark run should prefer machine-readable outputs over prose-only summaries.

Planned artifacts:

- `.binlog` build traces
- benchmark JSON/CSV/markdown summaries
- generated manifest or fixture metadata describing operation/project counts
- invalidation reports showing which outputs changed between runs
- short human-readable summaries that link back to the machine-readable artifacts

These artifacts should be suitable for both CI review and AI-agent consumption.

## Observability and debuggability

This module should make performance regressions explainable, not just detectable.

Prefer outputs that can answer:

- which stage got slower?
- which edit invalidated too much?
- how many operations/resources/projects were in the scenario?
- was the regression compile-time, startup-time, or request-time?
- did the reference app stay fast while the synthetic fixture regressed, or vice versa?

If a new module materially changes compile-time or runtime cost, it should also update the relevant scale harness and artifact contract.

## Explicit decisions

- Treat compile-time, IDE/build, and runtime scale as architectural requirements, not polish.
- Use deterministic scale invariants in ordinary CI and keep noisy wall-clock comparisons in scheduled/manual lanes.
- Make cacheability and invalidation locality first-class regression gates.
- Prefer synthetic generated fixtures for large-scale stress work over bloating the human-facing reference app.
- Expect every major module to declare how it participates in the scale harness once implemented.
- Treat known O(n²) or obviously linear-scan registry hot paths as redesign candidates before claiming Signature365-scale readiness.

## Current migration status

- The first runtime-hotspot measurement slice is now active in `src\Blueprint.SourceGenerator.Benchmarks\BlueprintApp\BlueprintAppBenchmarks.cs`.
- `ShortRun` BenchmarkDotNet artifacts now exist for `BlueprintApp.Register(...)` and `BlueprintApp.TryFindOperation(...)` under `BenchmarkDotNet.Artifacts\results\Blueprint.SourceGenerator.Benchmarks.RuntimeModel.BlueprintApp*report.*`.
- The current `ShortRun` registration baseline shows `RegisterAllOperations` at about `2.9 us` / `5.27 KB` for 10 operations, about `203.6 us` / `226.95 KB` for 100 operations, and about `1.08 ms` / `1296.4 KB` for 250 operations.
- The current `ShortRun` lookup baseline shows registered/missing `TryFindOperation(...)` calls at about `136-141 ns` for 10 operations, about `829-963 ns` for 100 operations, and about `1.76-2.02 us` for 250 operations, each with about `128 B` allocated.
- This slice is intentionally measurement-only. It captures the current `BlueprintApp` cost curve before any runtime optimization work changes the hot paths.
- The first deterministic compile-time fixture slice is now active in `src\Blueprint.SourceGenerator.Tests\Scale\SyntheticBlueprintFixtureGenerator.cs`.
- `src\Blueprint.SourceGenerator.Tests\Scale\SyntheticBlueprintFixtureGeneratorTests.cs` now proves repeated-output stability, expected app/operation file emission, and descriptor-generator compatibility through the existing `TestHelpers.Compile<OperationDescriptorSourceGenerator>(fixture.CombinedSource)` path, and `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --no-restore` passes after the helper lands.
- Next follow-on for the first milestone: add a multi-source compile harness and Tier 2 cacheability/invalidation-locality tests that apply single-operation and shared-contract mutations to the synthetic fixture surface, then revisit the measured `BlueprintApp` hot paths with that broader harness in place.
- Completed first Tier 2 multi-source harness slice: `SyntheticBlueprintFixtureGenerator` now emits partial query operations with inline handlers so the same synthetic fixture can drive descriptor, app-registration, and pipeline generation together; `TestHelpers` now exposes a tracked multi-generator harness for multi-source compile runs plus same-driver edit comparisons; and `SyntheticBlueprintFixtureScaleHarnessTests.cs` now proves a 100-operation fixture compiles cleanly through `OperationDescriptorSourceGenerator`, `AppDataModelSourceGenerator`, and `OperationExecutorPipelineSourceGenerator`.
- Completed first locality proof for that harness slice: mutating one synthetic operation's handler signature now leaves descriptor and app-registration outputs unchanged while changing only `SyntheticOperation050ExecutorPipeline.g.cs`, giving the workstream its first deterministic single-operation invalidation-locality assertion on the shared synthetic fixture surface.
- Completed shared-contract invalidation-locality follow-on: the synthetic fixture now emits a shared inherited `SyntheticOperationBase` validation contract with a deterministic source-file mutation seam, and `SyntheticBlueprintFixtureScaleHarnessTests.cs` now proves that adding `[MaxLength(5)]` to that shared inherited contract keeps descriptor and app-registration outputs stable while changing every generated executor pipeline file for the 100-operation fixture.
- Completed first `BlueprintApp` runtime-hotspot redesign: `BlueprintApp.Register(...)` now uses an indexed URL map for duplicate URL detection, `TryFindOperation(...)` now reuses the existing exact-type dictionary, the existing `BlueprintAppTests` behavior suite still passes, and the benchmark harness now reports registration at about `1.180 us` / `3 KB` for 10 operations, about `10.747 us` / `28 KB` for 100 operations, and about `29.750 us` / `63.39 KB` for 250 operations, while lookup stays around `5.6-7.5 ns` with zero allocations.
- Active next slice: add runtime startup and endpoint-mapping measurements for Blueprint HTTP hosts, using `ApplicationBuilderExtensions` as the current runtime surface while keeping the updated `BlueprintApp` benchmark artifacts as the comparison baseline.

## Sequencing

Start this work in Phase 0 and keep it evolving alongside implementation.

The first milestone is not a giant benchmark suite. It is a deterministic compile-time scale harness that proves the generator remains localized as modules are added.

Runtime and large-solution timing coverage should then deepen alongside pipeline, validation, auth, HTTP, and OpenAPI implementation so the benchmark surface grows with the architecture rather than lagging a whole phase behind it.

# Blueprint source-generated migration orchestrator plan

## Purpose

This is the master execution plan for moving the legacy Blueprint library and its Signature365 usage model onto the source-generated Blueprint stack in this repository.

Use this file as the **ordering, dependency, and delivery contract** for future implementation work. The detailed design intent still lives in the module plans under `migration\*\plan.md`, but this document decides:

- what gets done first
- which workstreams can run in parallel
- which gates must close before later phases begin
- what "done" means for each workstream
- what tests, benchmarks, artifacts, and docs are mandatory
- how an AI orchestrator should delegate work to sub-agents and reviews

The target is **consumer-contract compatibility**, not byte-for-byte parity with the legacy frame-based runtime. The new stack may generate very different code as long as it preserves the important consumer semantics and is more deterministic, inspectable, and AOT-friendly.

## Source material this plan consolidates

- `migration\plan.md` and all module plans under `migration\`
- `docs\guiding-principles.md`
- `docs\architecture.md`
- `docs\compile-time-validation.md`
- `docs\observability-and-dev-experience.md`
- `docs\refactoring-workflow.md`
- `docs\implementation-checklists.md`
- the current source-generator/runtime code called out in the existing migration documents

## Historical progress highlights and orientation

- Live migration state now lives only in `migration\progress.json`.
- The bullets in this section are historical context and rationale, not a competing live progress tracker.
- Planning and orchestration guidance are complete.
- `migration\progress.json` is the single source of truth for live migration state; it must stay aligned with the durable rationale and acceptance criteria recorded in `migration\plan.md` and the active workstream plan whenever the ready slice or review-gate state changes.
- The launcher contract is now stricter: once the next dependency-safe slice is identified, the same session must continue into failing proof and the first implementation step, then keep working through up to 5 related slices per invocation with verify-review-commit loops between slices unless an explicit blocker or review gate stops the work.
- The initial minimum **Phase 0 / Workstream 1: Foundation module system** slice has landed, including contributor capability splits, deterministic execution-plan diagnostics, `LinkGenerationModule` contributor routing, planning models, and stage-aware pipeline variable validation.
- The next session should read `migration\progress.json` immediately after this file, then treat the active workstream and detailed plan recorded there as the immediate execution target while keeping `migration\plan.md` as the ordering and safety contract.
- The first approved production pipeline-contributor slice is now complete: `HandlerExecutionModule` routes handler invocation plus the produced `handler-result` variable through the activated pipeline seam while preserving the existing generated pipeline output and result wrapping.
- The failing proof for that slice lives in `src\Blueprint.SourceGenerator.Tests\Generators\Pipelines\OperationExecutorPipelineGeneratorIntegrationTests.cs`, and the full pipeline integration class continues to pass after the seam activation.
- The approved follow-on refinement to that slice is now complete locally: contributor methods are now seam-specific (`ContributeToPipeline` / `ContributeToDescriptor`), `PipelineContributorContext` uses `RegisterVariable(...)` plus implicit stage participation instead of explicit `AddContribution(...)`, `PipelineRenderStepKind` has been removed, and `HandlerExecutionModule` owns the handler-invocation rendering callback directly.
- Planning context classes now expose helper methods plus only a small safe read-only metadata surface instead of record-style bags of public mutable inner state, internal variable-registry naming now aligns with `RegisterVariable(...)` / `RequireVariable(...)`, and module-specific helper files now live beside their owning module inside the module folder.
- Concrete module implementations now follow the repo convention of `*Module` naming and live in per-module folders under `src\Blueprint.SourceGenerator\Modules`.
- The second approved production pipeline-contributor slice is now complete: `ValidationModule` detects DataAnnotations/IValidatableObject validation surfaces at compile time, emits `Validator.TryValidateObject(...)` in `PipelineStage.Validation`, and now short-circuits generated validation failures to the validation-failure result surface that is currently canonicalized as `ValidationFailedResult` while retaining `ValidationFailedOperationResult` as the legacy alias instead of throwing `Blueprint.Validation.ValidationException`.
- The failing proof for the validation slice also lives in `src\Blueprint.SourceGenerator.Tests\Generators\Pipelines\OperationExecutorPipelineGeneratorIntegrationTests.cs`, and the main generator test suite plus `Blueprint.SourceGenerator.Tests.Integration` both pass after the seam activation.
- The validation slice now carries the stricter cross-layer proof matrix required for future pipeline/module work: a full generated-pipeline snapshot, a compile-time invalid-configuration proof (`BPV001` for invalid `CompareAttribute` property references), and runtime integration execution tests that prove invalid and valid validation paths against the generated pipeline. Simple substring assertions may remain as supplemental checks, but they are not sufficient to close a contributor slice on their own.
- Human-approved next follow-on after commit: stay within the validation slice long enough to add a reusable benchmark harness plus an opt-in Required-first fast path, using the legacy Blueprint property-level validation approach and the .NET options validation generator as inspiration. Keep exception translation deferred until that performance-oriented validation follow-on is review-closed.
- The validation performance follow-on is now review-closed and ready to commit: `src\Blueprint.SourceGenerator.Benchmarks` provides reusable BenchmarkDotNet coverage for generated validation pipelines, and `ValidationModule` now emits a narrow Required-only fast path for exact default `RequiredAttribute` shapes while falling back to generic object validation for mixed built-ins, `IValidatableObject`, display-name shaping, and unsupported Required customizations.
- The current `ShortRun` benchmark artifact (`BenchmarkDotNet.Artifacts\results\Blueprint.SourceGenerator.Benchmarks.Validation.ValidationPipelineBenchmarks-report.*`) shows the Required-only fast path reducing the valid path to about `321 ns` / `664 B` and the invalid Required-only paths to about `7.0-7.2 us` / `1464 B`, compared with the generic invalid Required fallback at about `9.5 us` / `2648 B`.
- The review-closeout pass for that slice is also complete: repo guidance now codifies high-signal comments, `static` no-capture lambdas, and module-specific test-file placement; `ValidationModule` has explicit fast-path/fallback rationale comments; module-owned source tests now live in `src\Blueprint.SourceGenerator.Tests\Modules\ValidationModuleTests.cs`; runtime execution proofs live in `src\Blueprint.SourceGenerator.Tests.Integration\Generators\Pipelines\ValidationModuleIntegrationTests.cs`; and the full format/build/test matrix passes after that refactor.
- The next validation optimization slice is now complete locally: `ValidationModule` also fast-paths default string-backed `[Required]` + `[MaxLength(n)]` combinations while continuing to fall back for display-name shaping, custom validation messages, unsupported `MaxLengthAttribute` customizations, non-string max-length usage, `IValidatableObject`, and other mixed built-ins that still need framework-owned validation semantics.
- The updated `ShortRun` benchmark artifact (`BenchmarkDotNet.Artifacts\results\Blueprint.SourceGenerator.Benchmarks.Validation.ValidationPipelineBenchmarks-report.*`) now adds `RequiredMaxLengthValidAsync` at about `176 ns` / `584 B`, `RequiredMaxLengthTooLongAsync` at about `6.1 us` / `1320 B`, and `RequiredMaxLengthWhitespaceAsync` at about `6.7 us` / `1200 B`.
- The proof set for the `Required` + `MaxLength` follow-on now includes module-owned generated-source assertions, a full generated-pipeline snapshot, runtime integration execution tests for whitespace, too-long, and valid paths, benchmark coverage in `src\Blueprint.SourceGenerator.Benchmarks\Validation\ValidationPipelineBenchmarks.cs`, and a passing format/build/test matrix (`Blueprint.SourceGenerator.Tests`, `Blueprint.SourceGenerator.CodeWriter.Tests`, and `Blueprint.SourceGenerator.Tests.Integration`).
- The live executor pipeline generator still hard-codes the outer logging plus exception/result shell, but both validation rendering and handler invocation rendering now live with contributor-owned render steps instead of the central pipeline generator.
- Human-approved next slice after this commit: stay on the validation optimization track, but change the plan of attack from operation-level all-or-nothing fast paths to **per-property planning**. Introduce a public descriptor property-attribute access seam (for example `GetPropertyAttributes(...)`), a shared runtime helper such as `ValidationRuntime.ValidateProperty(...)`, and extracted fast-path strategy/planning helpers outside `ValidationModule`.
- The approved boundary for that redesign is: fallback stays atomic per property, type-level validation attributes and `IValidatableObject` stay on the existing whole-object fallback path for now, and the first larger implementation batch should cover `Required`, `MaxLength`, `MinLength`, `StringLength`, and `EmailAddress`, with benchmark coverage for each fast path versus runtime fallback.
- The per-property validation redesign is now complete locally: `ApiOperationDescriptor` exposes `GetPropertyAttributes(...)`, `Blueprint.Validation.ValidationRuntime` provides the shared per-property runtime fallback with Required-first semantics, `ValidationModule` plans each property independently through extracted strategy helpers beside the module, and supported fast-path families now include `Required`, `MaxLength`, `MinLength`, `StringLength`, and `EmailAddress` while type-level validation attributes and `IValidatableObject` remain on the whole-object fallback path.
- The updated `ShortRun` benchmark artifact (`BenchmarkDotNet.Artifacts\results\Blueprint.SourceGenerator.Benchmarks.Validation.ValidationPipelineBenchmarks-report.*`) now measures the new fast-path versus runtime-fallback pairs: Required runtime fallback is about `6.9 us` / `2800 B` versus the fast invalid Required path at about `4.6 us` / `1552 B`; MaxLength fast is about `6.4 us` / `1408 B` versus runtime fallback at about `10.1 us` / `3104 B`; MinLength fast is about `5.7 us` / `1496 B` versus runtime fallback at about `9.8 us` / `3248 B`; EmailAddress fast is about `6.6 us` / `1432 B` versus runtime fallback at about `11.6 us` / `3024 B`; and StringLength currently stays roughly comparable on mean time in this short run (`8.1 us` fast versus `7.4 us` fallback) while still cutting allocations by more than half (`1448 B` versus `3216 B`).
- The proof set for the redesign now includes module-owned generated-source assertions, a reviewed mixed fast-path/runtime-fallback pipeline snapshot, runtime integration coverage for the new fast-path families plus direct descriptor/runtime-helper seam tests, benchmark coverage in `src\Blueprint.SourceGenerator.Benchmarks\Validation\ValidationPipelineBenchmarks.cs`, and a clean build plus passing `Blueprint.SourceGenerator.Tests`, `Blueprint.SourceGenerator.CodeWriter.Tests`, and `Blueprint.SourceGenerator.Tests.Integration`.
- The first retained Blueprint-specific cross-property follow-on is now complete and committed: `Blueprint.Validation.GreaterThanPropertyAttribute` is available as a consumer-facing contract, `ValidationModule` emits a direct property-to-property comparison fast path for supported same-type non-nullable numeric/decimal/`DateTime` properties, and unsupported shapes continue to flow through the shared per-property `ValidationRuntime.ValidateProperty(...)` fallback.
- `BPV002` is now reserved for invalid `GreaterThanPropertyAttribute` property references, so both `CompareAttribute` and `GreaterThanPropertyAttribute` missing-member contracts are rejected at build time.
- The updated `ShortRun` benchmark artifact (`BenchmarkDotNet.Artifacts\results\Blueprint.SourceGenerator.Benchmarks.Validation.ValidationPipelineBenchmarks-report.*`) now adds `GreaterThanPropertyInvalidAsync` at about `2.6 us` / `1.91 KB`, compared with `GreaterThanPropertyRuntimeFallbackInvalidAsync` at about `3.9 us` / `2.84 KB`.
- The next retained Blueprint-specific validation slice is also complete and committed: `Blueprint.Validation.GreaterThanAttribute` is available again as a consumer-facing contract, `ValidationModule` emits a direct `operation.Property <= constant` fast path for supported default-constructor non-nullable numeric/decimal shapes, and unsupported/customized shapes continue to flow through the shared per-property `ValidationRuntime.ValidateProperty(...)` fallback.
- The updated `ShortRun` benchmark artifact (`BenchmarkDotNet.Artifacts\results\Blueprint.SourceGenerator.Benchmarks.Validation.ValidationPipelineBenchmarks-report.*`) now also captures `GreaterThanInvalidAsync` at about `1.7 us` / `1.76 KB` versus `GreaterThanRuntimeFallbackInvalidAsync` at about `2.2 us` / `2.62 KB`, giving the retained-validator track a direct value-comparison benchmark alongside the existing property-to-property benchmark.
- The repo-local annotation-hygiene follow-on is now also complete and committed: `.agents\skills\annotation-hygiene\SKILL.md` captures the approved JetBrains-annotation guidance, and the first focused runtime-model, planning, and validation annotation passes are now in the tree.
- The retained conditional-required follow-on is now also complete and committed: `Blueprint.Validation.RequiredIfAttribute` and `RequiredIfNotAttribute` are available again as consumer-facing contracts, the per-property planner treats them as required-like rules so later validators short-circuit correctly, and `ValidationModule` emits direct conditional required checks for supported default-constructor scalar/null comparisons while falling back to the shared per-property `ValidationRuntime.ValidateProperty(...)` seam for arrays, custom messages, display-name shaping, and unsupported comparison shapes.
- `BPV003` and `BPV004` are now reserved for invalid `RequiredIfAttribute` and `RequiredIfNotAttribute` property references, extending the retained-validator compile-time guardrails to the remaining conditional required family.
- The updated `ShortRun` benchmark artifact (`BenchmarkDotNet.Artifacts\results\Blueprint.SourceGenerator.Benchmarks.Validation.ValidationPipelineBenchmarks-report.*`) now also captures `RequiredIfInvalidAsync` at about `2.8 us` / `2.7 KB` versus `RequiredIfRuntimeFallbackInvalidAsync` at about `4.8 us` / `3.23 KB`; `RequiredIfNotInvalidAsync` is about `4.3 us` / `2.98 KB` versus `RequiredIfNotRuntimeFallbackInvalidAsync` at about `5.3 us` / `3.49 KB`, so the current short run now shows both the expected allocation drop and a mean-time win for the retained conditional-required paths.
- The retained conditional-forbid follow-on is now also complete and committed: `Blueprint.Validation.NotAllowedIfAttribute` is available again as a consumer-facing contract, `ValidationModule` emits a direct conditional forbidden-value check for supported default-constructor scalar comparisons, and unsupported shapes continue to flow through the shared per-property `ValidationRuntime.ValidateProperty(...)` seam for display-name shaping, array dependent values, null dependent values, and other fallback-only contracts.
- `BPV005` is now reserved for invalid `NotAllowedIfAttribute` property references, completing the retained-validator compile-time guardrails for the property-dependent validation family.
- The updated `ShortRun` benchmark artifact (`BenchmarkDotNet.Artifacts\results\Blueprint.SourceGenerator.Benchmarks.Validation.ValidationPipelineBenchmarks-report.*`) now also captures `NotAllowedIfValidAsync` at about `3.4 us` / `2.14 KB`, `NotAllowedIfInvalidAsync` at about `3.8 us` / `3.04 KB`, and `NotAllowedIfRuntimeFallbackInvalidAsync` at about `5.7 us` / `3.41 KB`, so this retained-validator slice continues to show the expected invalid-path allocation drop while the short run now also shows a mean-time win over the display-name fallback path.
- Next slice after these retained-validator and annotation-hygiene follow-ons: move on to the deferred no-exception validation-failure redesign.
- The deferred no-exception validation-failure redesign is now complete locally: generated validation branches in `ValidationModule` short-circuit directly to the canonical `ValidationFailedResult` (with `ValidationFailedOperationResult` retained as a compatibility alias) after marking `otel.status_code=OK`, while `PipelineCodeGenerator` retains compatibility translation catches for legacy `Blueprint.Validation.ValidationException` callers and framework `System.ComponentModel.DataAnnotations.ValidationException` runtime-fallback scenarios.
- The updated `ShortRun` benchmark artifact (`BenchmarkDotNet.Artifacts\results\Blueprint.SourceGenerator.Benchmarks.Validation.ValidationPipelineBenchmarks-report.*`) now shows the expected invalid-path win from removing generated validation exceptions: `RequiredOnlyInvalidAsync` is about `3.2 us` / `2.27 KB` versus `RequiredFallbackInvalidAsync` at about `6.7 us` / `3.36 KB`, `RequiredRuntimeFallbackInvalidAsync` is about `3.8 us` / `2.48 KB`, `RequiredIfInvalidAsync` is about `2.8 us` / `2.7 KB` versus `RequiredIfRuntimeFallbackInvalidAsync` at about `4.8 us` / `3.23 KB`, `NotAllowedIfInvalidAsync` is about `3.8 us` / `3.04 KB` versus `NotAllowedIfRuntimeFallbackInvalidAsync` at about `5.7 us` / `3.41 KB`, and the whole-object `ValidatableObjectFallbackInvalidAsync` path now measures about `3.0 us` / `2.28 KB`.
- This remains a narrow seam-activation step, not a freeze of the broader result-model direction. The current result-wrapping behavior is intentionally preserved while host-agnostic result redesign stays deferred.
- The first Phase 0 / Workstream 3 runtime-hotspot baseline is now complete locally: `src\Blueprint.SourceGenerator.Benchmarks\BlueprintApp\BlueprintAppBenchmarks.cs` adds `ShortRun` BenchmarkDotNet coverage for `BlueprintApp.Register(...)` and `BlueprintApp.TryFindOperation(...)` at 10, 100, and 250 operation counts.
- The new hotspot artifacts (`BenchmarkDotNet.Artifacts\results\Blueprint.SourceGenerator.Benchmarks.RuntimeModel.BlueprintAppRegistrationBenchmarks-report.*` and `BenchmarkDotNet.Artifacts\results\Blueprint.SourceGenerator.Benchmarks.RuntimeModel.BlueprintAppLookupBenchmarks-report.*`) currently show registration growing from about `2.9 us` / `5.27 KB` at 10 operations to about `203.6 us` / `226.95 KB` at 100 and about `1.08 ms` / `1296.4 KB` at 250, while lookup remains about `136-141 ns` at 10 operations, about `829-963 ns` at 100, and about `1.76-2.02 us` at 250.
- Next recommended Phase 0 follow-on after this baseline: stay in Workstream 3 long enough to add the deterministic synthetic fixture generator plus Tier 2 cacheability/invalidation-locality coverage before attempting any `BlueprintApp` runtime optimization.
- The first retained Tier 2 compile-harness follow-on is now complete locally: `SyntheticBlueprintFixtureGenerator` now emits partial query operations with inline handlers so the same fixture can drive descriptor, app-registration, and pipeline generation together, `TestHelpers` now exposes a shared tracked multi-generator harness for multi-source compile runs and same-driver edit comparisons, and `SyntheticBlueprintFixtureScaleHarnessTests` now proves a 100-operation synthetic fixture compiles cleanly through all three generators while a single-operation handler-signature mutation only changes the expected pipeline output.
- The shared-contract invalidation-locality follow-on is now complete locally: `SyntheticBlueprintFixtureGenerator` now emits a shared inherited `SyntheticOperationBase` validation contract plus a generic source-file mutation seam, `SyntheticBlueprintFixtureGeneratorTests` now cover the shared base emission, and `SyntheticBlueprintFixtureScaleHarnessTests` now prove that adding `[MaxLength(5)]` to the shared inherited validation contract keeps descriptor and app-registration outputs stable while changing every generated executor pipeline file for the 100-operation synthetic fixture.
- The first `BlueprintApp` runtime-hotspot redesign is now complete locally: `BlueprintApp.Register(...)` uses an indexed URL map instead of scanning every registered descriptor for duplicate URL detection, and `TryFindOperation(...)` now reuses the existing exact-type operation dictionary while preserving the current runtime-model behavior tests.
- The updated `ShortRun` benchmark artifacts (`BenchmarkDotNet.Artifacts\results\Blueprint.SourceGenerator.Benchmarks.RuntimeModel.BlueprintAppRegistrationBenchmarks-report.*` and `BenchmarkDotNet.Artifacts\results\Blueprint.SourceGenerator.Benchmarks.RuntimeModel.BlueprintAppLookupBenchmarks-report.*`) now show registration at about `1.180 us` / `3 KB` for 10 operations, about `10.747 us` / `28 KB` for 100 operations, and about `29.750 us` / `63.39 KB` for 250 operations; lookup now stays around `5.6-7.5 ns` with zero allocations across the same operation counts.
- The first Blueprint HTTP startup/endpoint-mapping measurement follow-on is now complete locally: `BlueprintEndpointMappingBenchmarks` exercises the current `MapBlueprintApi(...)` host surface via a benchmark-specific Blueprint app and route builder inside `src\Blueprint.SourceGenerator.Benchmarks\BlueprintHttp\BlueprintEndpointMappingBenchmarks.cs`, and the fresh `ShortRun` artifact reports about `36.92 us` / `42.62 KB` for 10 operations, about `300.91 us` / `415.4 KB` for 100 operations, and about `739.42 us` / `1021.98 KB` for 250 operations.
- The generated executor dispatch measurement follow-on is now complete and human-approved: `BlueprintAppExecutor` exposes a shared `ExecuteAsync(ApiOperationContext)` shell in `src\Blueprint.Runtime\Exec\BlueprintAppDataModel.cs`, `src\Blueprint.SourceGenerator.Benchmarks\ExecutorDispatch\ExecutorDispatchBenchmarks.cs` now benchmarks registered-pipeline lookup plus direct-pipeline versus executor-shell dispatch at 10, 100, and 250 operations, and the fresh `ShortRun` artifact (`BenchmarkDotNet.Artifacts\results\Blueprint.SourceGenerator.Benchmarks.RuntimeExec.ExecutorDispatchBenchmarks-report.*`) reports lookup at about `15.5 ns`, `15.9 ns`, and `24.1 ns`; direct pipeline execution at about `69.1 ns`, `55.6 ns`, and `91.4 ns` with `184 B` allocated; and full executor-shell dispatch at about `93.1 ns`, `97.5 ns`, and `137.8 ns` with the same `184 B` allocation profile.
- Human-approved next Phase 0 follow-on after the dispatch benchmark: switch the active detailed plan to `migration\generated-manifests-and-inspection-surfaces\plan.md` and land the shared manifest envelope, naming/layout rules, and contributor ownership contract before multiple workstreams invent incompatible artifact formats.
- The first manifest contract-definition pass is now complete and human-approved: `src\Blueprint.SourceGenerator\Artifacts\GeneratedManifestEnvelope.cs` defines the canonical build-time envelope, default `.blueprint/manifests/<artifact>.manifest.json` layout, app/generator identity fields, contributor-owned top-level sections, duplicate operation-key rejection, and stable ordinal key ordering for deterministic serialization; `src\Blueprint.SourceGenerator.Tests\Artifacts\GeneratedManifestEnvelopeTests.cs` plus `GeneratedManifestEnvelopeTests.CreateBuildManifest_Should_Serialize_Stable_Envelope.verified.txt` lock the approved contract.
- The first real manifest-emitting contributor slice is now human-approved: `IDebugManifestContributor` now exposes `ContributeToManifest(DebugManifestContributorContext)`, `BlueprintSourceGenerationConfig.BuildDebugManifest(...)` activates the seam, `OperationDiscoveryManifestModule` contributes `kind`/`namespace`/`typeName` discovery entries for each operation, and `OperationsDiscoveryManifestEmitter` projects that planning data into the approved `operations.manifest.json` envelope shape with an `operations-discovery` section plus deterministic per-operation summaries. The slice is hardened by namespace-safe `Blueprint.Model` interface classification plus `BPMOD005` missing-operation diagnostics, the latest format/build/main-generator-tests pass is green, and second-pass code review found no remaining blocking issues.
- Human review direction for future manifest work: the current operations-discovery entry shape is approved as the starting projection, but it is not permanently frozen. Later debug-manifest improvements may diverge from the current `kind`/`namespace`/`typeName` payload when a better owned shape is warranted, as long as those changes go back through the same review/versioning gates.
- The first build-time projection follow-on is now human-approved: `GeneratedManifestEnvelope.ToMarkdown()` projects deterministic markdown from the shared envelope, `ManifestMarkdownProjectionTests` lock a stable operations summary plus an envelope-only guard, and nested object payloads are now normalized recursively so richer future manifest data does not reintroduce insertion-order markdown diffs.
- Human-approved next Phase 0 follow-on after the markdown slice: start runtime inspection surfaces with the narrowest projection first by loading the approved manifest envelope from embedded runtime resources. This embedded-resource slice should establish runtime access to pre-built manifest data before dev-only endpoints or CLI projections widen the inspection surface.
- The manifest inspection workstream has since advanced beyond that first embedded-resource review gate: the embedded JSON seam, development-only JSON/HTML/markdown endpoints, debug-manifest collision diagnostic, and initial parser-hardening slices now live in `migration\generated-manifests-and-inspection-surfaces\plan.md` as the durable per-slice record.
- The latest schema-validation follow-on is now complete locally: nested required-string failures now report full manifest paths for `app`, `generator`, and section contributor identities, `GeneratedManifestEnvelopeTests` cover those invalid-token paths, and the latest format/build/main-generator-tests pass with 185 tests green.
- The machine-readable schema-validation follow-on is now also complete locally: `src\Blueprint.SourceGenerator\Artifacts\GeneratedManifestEnvelope.schema.json` now defines the approved envelope contract, `GeneratedManifestEnvelopeTests` validate the embedded operations manifest and an invalid `app.typeName` mutation against it, `ManifestInspectionEndpointTests` semantically pin the hosted markdown fixture to the canonical JSON projection, and the latest format/build/main-generator-tests pass with 188 tests green.
- The user has now approved autonomous Phase 0 direction, so the manifest review gate was closed in favor of the runtime-inspection branch first. That hosted-schema slice is now complete locally: `MapBlueprintApi(...)` exposes a development-only `/.blueprint/manifests/operations.schema.json` route when `BlueprintHttpOptions.OperationsManifestSchemaResourceName` is configured, `ManifestSchemaInspectionEndpoint` serves the embedded schema bytes unchanged, and `ManifestInspectionEndpointTests` cover the new route plus fail-fast invalid-resource wiring.
- A full verification pass completed cleanly for the hosted-schema slice too: the focused schema-endpoint proof passes, `dotnet format .\Blueprint.sln --no-restore --include src\Blueprint.Runtime\Http\BlueprintHttpOptions.cs src\Blueprint.Runtime\Http\ApplicationBuilderExtensions.cs src\Blueprint.Runtime\Http\ManifestSchemaInspectionEndpoint.cs src\Blueprint.SourceGenerator.Tests\Artifacts\ManifestInspectionEndpointTests.cs` is clean aside from the repo's known IDE1006 noise, `dotnet build .\Blueprint.sln --no-restore` succeeds, and `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --no-restore` passes with 190 tests green.
- Human-approved next Phase 0 follow-on after the hosted-schema slice: start the first cross-artifact alignment step by emitting a machine-readable invalidation report for the Tier 2 synthetic scale harness through the shared manifest envelope, so performance artifacts begin following the same reviewable contract before broader benchmark/telemetry publishing grows.

## Durable decision rule

- `migration\progress.json` is the single source of truth for live migration state: active workstream, next ready slice, last completed slice, and open review gate.
- `migration\plan.md` plus the relevant `migration\...\plan.md` file are the cross-session reference plans for ordering, acceptance criteria, rationale, and human/review decisions. They must not be treated as competing live status trackers.
- If `migration\progress.json` is missing, malformed, or points at a removed slice, repair it first from these plans before continuing.
- Session-local state such as chat history, session `plan.md`, SQL todos, or transient review artifacts is not sufficient to carry a decision into the next orchestrator session.
- Lack of an explicitly human-approved next slice is not, by itself, a reason to stop. The orchestrator should pick the narrowest dependency-safe next slice autonomously, persist that choice into `migration\progress.json` first, and then record the rationale here and in the relevant `migration\...\plan.md` file before continuing.
- If a human or review step approves a next slice, changes architectural direction, or closes a gate, update `migration\progress.json` first and then record the rationale here and in the relevant `migration\...\plan.md` file before the session ends.

## Non-negotiable engineering rules

- Keep pushing work from runtime into source generation, analyzers, and deterministic build-time models.
- Do not reintroduce runtime C# generation or runtime assembly compilation.
- Preserve the important consumer-facing contracts: operations, resources, HTTP attributes, auth/validation semantics, grouped-handler discovery, and result behavior.
- Prefer compile-time diagnostics over runtime surprises whenever the generator can know the answer.
- Keep the runtime AOT-friendly, reflection-light, and explainable.
- Treat observability, generated manifests, and AI-friendly inspection surfaces as part of the feature, not follow-up work.
- Keep the human-facing reference app curated and readable; use synthetic fixtures for scale and stress testing.
- Prefer deliberate breaking changes with analyzers/codemods over accidental compatibility drift.

## How a future orchestrator should use this plan

1. Read `migration\progress.json` first for the live state.
2. Read this file next for ordering, dependency, and safety-gate rules.
3. Read the detailed module plan named by `migration\progress.json` before making changes.
4. Do not start a workstream until every listed dependency is complete or an explicit human override is recorded.
5. Implement the smallest reviewable slice that can satisfy a meaningful acceptance criterion.
6. Do not stop after slice selection, doc reading, or plan/todo setup alone. In the same session, continue into failing proof and the first production edit unless an explicit blocker or review gate prevents it. When the slice lands cleanly, prefer to keep going through up to 5 related slices per invocation, always one-by-one with verify-review-commit checkpoints between them.
7. Follow the TDD-first execution loop below for every slice, using the repo-local `test-driven-development`, `systematic-debugging`, and `verification-before-completion` skills when relevant.
8. Update docs, machine-readable artifacts, and relevant Blueprint skill artifacts in the same change.
9. Use the sub-agent guidance in this file to split investigation, implementation, testing, and review work.
10. At every major design gate, request human review before declaring the phase complete. Outside those explicit gates, only escalate for human approval when the issue is absolutely critical and relevant different sub-agents still cannot agree on a safe next step.
11. After a slice reaches local done and passes self-review, generate a visual human-review handoff with `visual-explainer` before commit or before moving to the next phase gate.
12. Write that review artifact into the launcher-provided allow-listed review artifact directory (currently `$HOME\.agent\diagrams`) unless a human explicitly requests a different location.
13. Treat any feedback from that visual review handoff as input for another AI review/fix round before committing. If review closes a decision about sequencing, slice selection, or architecture direction, write that decision explicitly into `migration\progress.json` first and then into `migration\plan.md` before ending the session.
14. If a slice is obviously complete, review-closed, and verification-passing, make a small, focused git commit before switching to unrelated work or ending the session. If a review gate remains open, stop short of commit and record why.

## TDD-first execution loop

Every workstream and sub-task should follow this loop unless it is purely documentation-only:

1. Define the slice and copy its acceptance criteria into the task description.
2. Add a failing executable proof first:
   - unit test
   - snapshot test
   - integration test
   - analyzer/code-fix test
   - benchmark harness or telemetry capture
   - use `test-driven-development` discipline to prove the test fails for the right reason before production edits
   - for pipeline/module contributor slices that change generated behavior, the proof must include the full three-layer matrix before production edits:
     - a full pipeline snapshot covering the generated output shape
     - a compile-time invalid-configuration/source-check proof for symbol-backed or string-backed contracts the generator can validate
     - a runtime integration test that executes the generated pipeline through representative valid and invalid paths
     - simple `Contains(...)` or line-presence assertions may supplement the proof, but they cannot replace this matrix
3. Implement the smallest slice needed to make the proof pass.
4. Run targeted `dotnet format`, build, and the relevant tests.
5. If the proof or build behaves unexpectedly, use `systematic-debugging` discipline before proposing fixes.
6. Review the **generated diff**, not only the handwritten source diff.
7. Update docs, manifests, SARIF/JSON reports, or benchmark artifacts that changed.
8. Run a review agent for risky architectural or cross-cutting changes.
9. Create a `visual-explainer` review document for the completed slice, including architecture, key snippets, representative outputs/artifacts, review findings, and explicit feedback prompts for the human reviewer.
10. Use `verification-before-completion` discipline before any success or handoff claim: rerun the fresh verification commands, read the output, then report status.
11. If the slice is review-closed, create a small, focused git commit for it before moving on.
12. Only then move to the next slice.

If a slice cannot be tested first, the orchestrator must explain why and add the earliest possible automated proof immediately after the first implementation step.

When a review or design question must be asked, do not leave it as a raw question alone. Present:

1. the recommended answer
2. 2-3 concrete options when useful
3. consulted-agent input or dissent when it materially affects the recommendation

## Snapshot and diagnostic registration policy

- Snapshot baselines are never auto-approved just because tests pass.
- New or changed snapshot baselines require an intentional generated-diff review plus `code-review` agent or human sign-off before commit.
- If a slice introduces diagnostics, reserve the ID or ID range in `docs\compile-time-validation.md` before implementation starts.
- If a slice adds a new doc surface or changes documentation navigation, update `docs\README.md` in the same change.

## Global definition of ready

A work item is ready only when:

- [ ] All upstream dependencies listed in this plan are done.
- [ ] The relevant `migration\...\plan.md` file has been read.
- [ ] The target failing proof is chosen up front.
- [ ] Any new diagnostic IDs/ranges have been reserved in `docs\compile-time-validation.md`.
- [ ] Required docs and artifact updates are identified.
- [ ] Any ambiguity in the detailed module plan has been resolved by updating the plan or asking for human guidance.
- [ ] The slice is small enough to review in one pass.

## Global definition of done

A work item is done only when:

- [ ] All listed acceptance criteria are met.
- [ ] Relevant tests, snapshots, and/or benchmarks pass.
- [ ] Incremental-generator cacheability remains protected where the generator pipeline changed.
- [ ] `dotnet format` has been run on touched code.
- [ ] Generated diffs and snapshot changes were reviewed intentionally.
- [ ] New or changed snapshot baselines received `code-review` agent or human sign-off.
- [ ] Required docs were updated in the same change.
- [ ] `docs\README.md` was updated if documentation navigation or scope changed.
- [ ] Required machine-readable artifacts were produced or updated.
- [ ] A review agent has checked risky changes.
- [ ] A `visual-explainer` HTML review artifact was created after self-review and before commit/handoff.
- [ ] The visual review artifact includes architecture, key snippets, representative outputs/artifacts, review findings, open concerns, and feedback prompts for the human reviewer.
- [ ] A human review gate was requested if the work crossed a phase boundary or changed a public architectural contract.
- [ ] Any approved review or human decision that affects next steps or architecture direction was written into `migration\plan.md` and the relevant `migration\...\plan.md` file.
- [ ] If no review gate remained open, the completed slice was captured in a small, focused git commit, or the reason for not committing was recorded explicitly.

## Mandatory artifact contract

These outputs are part of the implementation, not optional extras:

- Generated source snapshot diffs for generator changes
- Visual HTML review documents for completed migration slices and phase gates, suitable for human review before commit
- Machine-readable manifests for operations/routes/auth/results/links as those surfaces land
- OpenAPI output for HTTP-capable phases
- Migration artifacts for consumer migration work:
  - SARIF diagnostics
  - codemod JSON report
  - short markdown summary
  - before/after manifest and equivalence reports
- Performance and scale artifacts:
  - `.binlog`
  - generator/analyzer timing summaries
  - invalidation-locality summaries
  - benchmark output for runtime hot paths
  - benchmark reports from `src\Blueprint.SourceGenerator.Benchmarks` when a touched slice changes a measured validation or pipeline hot path

## Phase order and hard gates

### Phase 0 - Foundation, migration safety, and scale

No downstream feature work should be treated as "real" until Phase 0 closes.

**Phase 0 exit gate**

- [x] Foundation module system minimum viable slice is implemented.
- [x] Migration diagnostic ranges, report classes, and analyzer/codemod direction are defined.
- [x] Tier 2 compile-time scale harness exists and is running deterministically.
- [x] Generated manifest envelope/schema contract is agreed and reviewable.
- [x] Benchmark baselines for the known `BlueprintApp` hotspots exist in a reviewable artifact location before Phase 1 starts.
- [x] The Phase 0 review artifact set exists: manifest schema/example, synthetic fixture example, Tier 2 test evidence, and hotspot benchmark baseline.

### Phase 1 - Core HTTP parity path

This phase ends when the repository can prove a coherent source-generated HTTP execution path, not just isolated generators. Full OpenAPI parity still closes in Phase 2, but Phase 1 must leave the architecture ready for HTTP-implied OpenAPI.

**Phase 1 exit gate**

- [ ] Operation discovery, pipeline composition, result model, error model, HTTP host, validation, auth, and observability all work together.
- [ ] The reference app is runnable with real handlers and deterministic seeded behavior.
- [ ] At least one end-to-end reference-app test exercises discovery -> pipeline -> result -> error -> HTTP host -> validation/auth/observability together.
- [ ] The re-query decision gate is explicitly closed.
- [ ] Generated inspection surfaces are good enough for humans and AI agents to inspect the app surface.
- [ ] The Phase 1 review artifact set exists: route/host manifest, auth/result manifest, generated diff review, and end-to-end test evidence.

### Phase 2 - Resource/API shape parity

This phase closes the richer API surface expected by consumer apps.

**Phase 2 exit gate**

- [ ] Resource metadata, link generation, post-processing, and resource-event behavior are implemented with a deliberate no-general-nested-execution story.
- [ ] OpenAPI is generated automatically from the same source-generated metadata used by the HTTP host.
- [ ] The reference app and artifacts demonstrate these capabilities end to end.
- [ ] The Phase 2 review artifact set exists: resource/link manifest, OpenAPI artifact, and post-processing/resource-event test evidence.

### Phase 3 - Non-HTTP hosts and extensibility

This phase proves that the architecture is general, not HTTP-only.

**Phase 3 exit gate**

- [ ] At least one non-HTTP host works through the shared module/contributor model.
- [ ] At least one consumer-space integration seam is proven with a realistic package-style example.
- [ ] The migration tooling and extension seams are credible enough for Signature365-style migrations beyond the reference app.
- [ ] The Phase 3 review artifact set exists: non-HTTP host manifest, a worked example that shows clear host/package separation in namespacing, and a realistic migration dry-run/report sample.

## Dependency summary

| Workstream | Depends on |
| --- | --- |
| Foundation module system | None |
| Breaking changes and migration tooling | Foundation design direction |
| Performance and scale | No hard blocker for first milestone; evolves with foundation/core work |
| Generated manifests and inspection surfaces | Foundation design direction |
| Operation discovery and descriptors | Foundation module system |
| Pipeline composition and execution | Foundation + operation discovery + provisional result contract from workstream 7; the approved narrow `HandlerExecutionModule` seam-activation slice may proceed earlier only if it preserves the current result-wrapping behavior and does not freeze the broader result-model direction |
| Result model and host translation | Foundation + operation discovery + pipeline design loop; must be agreed before pipeline/HTTP/error/OpenAPI contracts freeze |
| Error model and exception handling | Foundation + operation discovery + pipeline composition + result model |
| HTTP host and message population | Foundation + operation discovery + pipeline composition + result model |
| Validation | Foundation + operation discovery + pipeline composition |
| Authentication and authorisation | Foundation + operation discovery + pipeline composition + HTTP host |
| Auditing and observability | Foundation + shared shell design; starts early and evolves with Phase 1 modules |
| Reference app and end-to-end example | Pipeline composition + HTTP host for Layer 1; broadens with later modules |
| Resource model, links, and post-processing | Phase 1 core + re-query decision gate |
| OpenAPI | Phase 1 core + resource model/result/serializer decisions |
| Background tasks | Foundation + operation discovery + pipeline composition |
| Persistence and transaction coordination | Foundation + typed variable registry + pipeline composition |
| Messaging and integration hosts | Foundation + operation discovery + pipeline composition + background tasks |

## Global sub-agent guidance

### Required skill loading for .NET work

Before planning or implementation, load:

1. `using-dotnet`
2. `dotnet-advisor`
3. `dotnet-csharp`
4. `dotnet-tooling`, `dotnet-testing`, and/or `dotnet-devops` as the workstream requires
5. `using-blueprint` when the slice changes consumer-facing Blueprint usage, migration guidance, or the preferred sample-consumer story
6. `extending-blueprint` when the slice changes Blueprint generators, runtime contracts, modules, diagnostics, or repo-internal Blueprint guidance

For JetBrains annotation hygiene work inside this repository, load `annotation-hygiene` after `dotnet-advisor`/`dotnet-csharp` instead of extending one of the broader .NET skills.

### Blueprint skill contract

- `using-blueprint` is the canonical consumer-facing Blueprint skill. Keep it focused on consumer contract and workflow guidance, not installation or distribution instructions, unless a later migration slice explicitly scopes that work.
- `extending-blueprint` is the repo-local overlay for contributors changing Blueprint internals, migration guidance, or the preferred consumer story inside this repository.
- Repository ownership of these skills is sufficient for now. Do not invent a mirroring, export, or downstream distribution workflow until that work is explicitly planned and approved.

### Recommended sub-agent roles

- Use an `explore` agent first for large workstreams to synthesize the relevant migration docs, code paths, and existing tests.
- Use a `general-purpose` agent for multi-file implementation and architectural reasoning.
- Use a `task` agent for build, format, test, and benchmark commands so the main context stays clean.
- Use a `code-review` agent after risky architectural changes, before phase exits, and before merging broad migration-tooling changes.
- Always use the agent handoff contract below when delegating work.

### Recommended review pattern

For architecture, plans, or risky cross-cutting changes:

- one strong reasoning model from one family
- one strong reasoning model from a different family
- one faster/skeptical model to look for ambiguity and missing operational detail

The goal is not "three approvals". The goal is to surface blind spots before humans review.

### Agent handoff contract

When delegating a workstream, include:

- the relevant section from this plan
- the linked detailed module plan(s)
- explicit dependencies
- acceptance criteria
- failing-proof expectations
- required docs/artifacts
- known open questions or gates that must not be silently decided

### Escalation rules

- If the detailed module plan is too vague to produce executable acceptance criteria, stop and refine the plan or ask for human guidance before coding.
- If a dependency conflict appears between this file and a module plan, do not silently reinterpret the order; reconcile the plans or escalate.
- If a slice cannot start with a failing proof and it touches a public contract, security behavior, dependency order, or phase gate, get human approval before proceeding.
- If a change would weaken compile-time determinism, cacheability, or inspection-surface quality, treat that as a design issue, not an implementation shortcut.

## Workstreams in execution order

## 1. Foundation module system

**Priority:** P0  
**Detailed plan:** `migration\foundation-module-system\plan.md`  
**Depends on:** none

### Checklist

- [ ] Write and run a failing proof for the current slice before implementation.
- [ ] Split `IBlueprintSourceGeneratorModule` into capability-oriented contributor seams.
- [ ] Introduce planning models such as `OperationModel`, `DescriptorPlan`, `PipelinePlan`, `ExecutionShellPlan`, `AppRegistrationPlan`, and `GeneratedDebugManifest`.
- [ ] Reintroduce ordered pipeline stages: Setup, Population, Authentication, Authorisation, Validation, Execution, PostExecution, Cleanup.
- [ ] Add a typed variable registry for DI services, locals, context values, and cross-module produced values.
- [ ] Add cross-operation lookup so contributors can reason about referenced operations.
- [ ] Define module ordering, dependency declaration, and conflict diagnostics.
- [ ] Keep `LinkGenerationModule` as the first real contributor example; add a dedicated migration adapter only if another module truly needs staged compatibility support.

### Acceptance criteria

- [ ] Existing module behavior can flow through the new contributor model without ad-hoc generator hacks.
- [ ] Downstream modules can contribute descriptor, pipeline, host, app-registration, and debug-manifest data through explicit seams.
- [ ] Ordering conflicts and duplicate unique contributors fail deterministically at compile time.
- [ ] Typed variable production/consumption is explicit enough for persistence/messaging scenarios.
- [ ] Generator cacheability does not regress when direct contributor routing is enabled.

### Tests and benchmarks

- [ ] Unit tests for ordering rules, dependency edges, and variable-registry behavior.
- [ ] Tests for planning helpers cover happy paths, failure/diagnostic paths, and determinism edge cases.
- [ ] Snapshot tests for plan-model-driven output.
- [ ] Cacheability-sensitive tests using `TestHelpers.Compile<T>()`.
- [ ] Medium-scale fixture run proving the new foundation does not explode invalidation breadth.
- [ ] Integration test for the persistence/outbox worked example described in the foundation plan's cross-module variable-sharing guidance.

### Docs and artifacts

- [ ] Update `docs\architecture.md` with the contributor model and planning-model flow.
- [ ] Update `docs\implementation-checklists.md` if the new module pattern changes the standard workflow.
- [ ] Update `docs\compile-time-validation.md` if new diagnostic families are introduced.

### Durable progress

- The first foundation slice now introduces contributor capability interfaces, `BlueprintSourceGenerationConfig.BuildExecutionPlan()` ordering/dependency validation with `BPMOD001`-`BPMOD003`, initial planning models (`OperationModel`, `DescriptorPlan`, `PipelinePlan`, `AppRegistrationPlan`, `GeneratedDebugManifest`, and `VariableRegistry`), and a real-contributor conversion of `LinkGenerationModule` through the new execution plan.
- Planning and variable-registry coverage now exercises happy paths, failure/diagnostic paths, and determinism edge cases for the implemented foundation behaviors.
- Pipeline contributors now expose a typed `IPipelineContributor.ContributeToPipeline(PipelineContributorContext)` contract, descriptor contributors use `ContributeToDescriptor(...)`, and descriptor generation activates `ConfigurePipelinePlan()` so required variables fail deterministically with stage-aware `BPMOD004`.
- Planning context classes expose helper methods plus only a small safe read-only metadata surface instead of record-style bags of mutable inner state, module-specific helper files live beside the owning module, and variable registry terminology now aligns with `RegisterVariable(...)` / `RequireVariable(...)`.
- Validation now flows through `ValidationModule`, which activates only for operations with DataAnnotations validation attributes or `IValidatableObject`, emits `Validator.TryValidateObject(...)` in `PipelineStage.Validation`, and short-circuits directly to the canonical `ValidationFailedResult` for generated validation failures while leaving framework `System.ComponentModel.DataAnnotations.ValidationException` translation in the outer shell and retaining `ValidationFailedOperationResult` as a compatibility alias.
- The live pipeline now routes both validation and handler execution through contributor-owned render steps while keeping the outer logging plus exception/result shell hard-coded.
- Next recommended slice after the validation review gate: keep broader result normalization, host wiring, and debug-manifest work deferred while reviewing whether any additional compatibility cleanup around retained legacy validation surface should happen separately.

### Sub-agent guidance

- Use `explore` to trace current module seams and the legacy middleware/pipeline model.
- Use a strong `general-purpose` agent for the contributor API and planning-model design.
- Use `task` for focused test runs while iterating contributor activation slices.
- Require `code-review` before freezing any public contributor API.

## 2. Breaking changes and migration tooling

**Priority:** P0  
**Detailed plan:** `migration\breaking-changes-and-migration-tooling\plan.md`  
**Depends on:** foundation direction

### Checklist

- [ ] Write and run a failing proof for the current slice before implementation.
- [ ] Define the deliberate breaking changes and classify them by migration strategy.
- [x] Reserve and document migration diagnostic ranges such as `BPMIG001`-`BPMIG399`.
- [x] Plan and scaffold `Blueprint.Migration.Analyzers`.
- [ ] Plan and scaffold `Blueprint.Codemod`.
- [ ] Define SARIF, JSON, and markdown report schemas.
- [ ] Define compatibility-shim policy, lifetime, and removal gates.
- [ ] Align the execution workflow with `docs\refactoring-workflow.md`.

### Acceptance criteria

- [ ] The highest-value breaking changes are explicit rather than implicit.
- [ ] Safe auto-fixes have an analyzer/code-fix path.
- [ ] Larger stateful rewrites have a codemod dry-run/apply path.
- [ ] Report output is machine-readable, stable, and reviewable by humans and AI agents.
- [ ] A migrated consumer app can be classified into auto-fixable, configuration-required, consumer-extension rewrite, and manual-review buckets.

### Tests and benchmarks

- [x] Analyzer diagnostic tests.
- [x] Code-fix tests with before/after coverage.
- [x] JSON/SARIF schema validation tests.
- [x] Dry-run idempotency tests for the codemod CLI.
- [x] Sample-app migration report review before broad rollout.

### Docs and artifacts

- [x] Update `docs\refactoring-workflow.md` with the concrete migration workflow and artifact locations.
- [x] Update `docs\compile-time-validation.md` with migration diagnostic guidance.
- [x] Add examples of expected migration artifacts once the tooling lands.

### Durable progress

- the `BPMIG001`-`BPMIG399` range is now reserved in repo guidance, `Blueprint.Migration.Analyzers`, `Blueprint.Migration.Analyzers.CodeFixes`, and `Blueprint.Migration.Analyzers.Tests` now provide the first complete analyzer/code-fix/test seam, `BPMIG001` now flags plus safely removes redundant parameterless `BlueprintApiBuilder.AddOpenApi()` activation calls, `BPMIG002` now flags redundant parameterless `AddHateoasLinks()` activation calls, `BPMIG003` now flags redundant parameterless `AddAuthorisation()` activation calls, `BPMIG100` now flags configured `AddOpenApi(Action<OpenApiOptions>)` usage as configuration-required migration work, and `BPMIG101` now flags configured `AddAuthentication(...)` usage as configuration-required migration work.
- the shared repo-native migration-report contract now lives in `src\Blueprint.Migration.Analyzers\MigrationReport.cs`, `src\Blueprint.Migration.Analyzers\DiagnosticFinding.cs`, and `src\Blueprint.Migration.Analyzers.Tests\MigrationReportSerializationTests.cs`, with deterministic ordering, stable bucket strings, and durable documentation-path mapping for the current activation and auth/resource migration findings.
- the first repo-native codemod dry-run collector now lives in `src\Blueprint.Codemod\Blueprint.Codemod.csproj`, `src\Blueprint.Codemod\SolutionAnalysisRunner.cs`, and `src\Blueprint.Codemod.Tests\ActivationDiagnosticsReportCollectionTests.cs`, proving that a fixture solution can emit the shared migration report through temporary SARIF collection, including the first auth/resource finding once `[FromResource(...)]` is present.
- the first repo-native codemod apply-mode foundation now also lives in `src\Blueprint.Codemod\ApplyModeOptions.cs`, `src\Blueprint.Codemod\TransformationApplier.cs`, and `src\Blueprint.Codemod.Tests\ApplyModeTests.cs`, proving that dry-run planning results can drive the first supported activation code-fix replay (`BPMIG001`) through `MSBuildWorkspace` while keeping repeat runs idempotent and configuration-required findings intact.
- the first durable codemod artifact-output slice now also lives in `src\Blueprint.Codemod\CodemodArtifactWriter.cs`, `src\Blueprint.Codemod\CodemodMarkdownSummaryWriter.cs`, `src\Blueprint.Codemod\Program.cs`, and `src\Blueprint.Codemod.Tests\CodemodArtifactWriterTests.cs`, proving that dry-run and apply runs can keep emitting canonical stdout JSON while `--output <dir>` writes matching `codemod-report.json` and `codemod-report.md` artifacts sourced from the same `MigrationReport`.
- the first auth/resource migration diagnostic-reporting sub-slice is now complete: `BPMIG200` flags legacy `[FromResource(...)]` usage as consumer-extension rewrite work, `LegacyActivationAnalyzer` reports it at the attribute location, `DiagnosticFinding` routes it to `migration\authentication-and-authorisation\plan.md`, and the shared codemod fixture/report/artifact coverage now carries that finding through dry-run and apply output.
- the next auth/resource migration diagnostic-reporting sub-slice is now also complete: `BPMIG201` flags raw `{Auth:...}` placeholder usage at the template literal, routes that finding to `migration\authentication-and-authorisation\plan.md`, and keeps the placeholder finding visible in dry-run/apply/artifact output alongside `BPMIG200`.
- the shared report-family rename is now also complete: `MigrationReport.ReportType`, the main report factory/callers, and the codemod artifact expectations now use `migration-diagnostics`, so the shared report surface no longer advertises activation-only semantics while carrying auth/resource findings.
- the JSON contract-validation follow-on is now also complete: `MigrationReportSerializationTests` and `CodemodArtifactWriterTests` both assert the required `migration-diagnostics` top-level and finding-level JSON shape, adding an explicit drift guard without changing the payload itself.
- the SARIF contract-validation follow-on is now also complete: `SarifDiagnosticReader` gives the dry-run collector a supported input-validation seam, `SarifDiagnosticReaderTests` guard valid rule-ID projection plus actionable failures for missing `runs` and malformed `ruleId` values, and `SolutionAnalysisRunner` now shares that seam so malformed analyzer output cannot silently drop findings.
- the dry-run idempotency follow-on is now also complete: `Program.RunAsync` exposes a supported writer-injectable orchestration seam for tests, `ProgramCancellationTests` no longer use reflection to reach the CLI path, and `DryRunIdempotencyTests` now prove repeated review-only runs keep stdout, durable artifacts, and source files stable for the same copied fixture.
- the sample-app migration report review gate is now also complete: the `SimpleBlueprintApp` fixture now has a preserved dry-run review package with `codemod-report.json`, `codemod-report.md`, and captured stdout JSON, and that package shows the expected current finding mix (1 auto-fixable, 2 configuration-required, 2 consumer-extension rewrite, 0 manual-design-review) without surfacing a new tooling or contract blocker.
- next approved cross-workstream follow-on after the tooling review gate: return to `migration\performance-and-scale\plan.md` and publish the existing benchmark/telemetry outputs in a reviewable format using the already-approved manifest and scheduled/manual-lane contracts.

### Sub-agent guidance

- Use `explore` to inventory legacy activation APIs, nested execution assumptions, validation attributes, and grouped-handler usage.
- Use a `general-purpose` agent for analyzer/codemod design.
- Use a testing-focused agent or `task` runs for analyzer/code-fix test suites.
- Require `code-review` before any analyzer is allowed to rewrite code at solution scale.

## 3. Performance and scale

**Priority:** P0  
**Detailed plan:** `migration\performance-and-scale\plan.md`  
**Depends on:** no hard blocker for the first milestone; then evolves with foundation/core work

### Checklist

- [x] Write and run a failing proof for the current slice before implementation.
- [x] Build a deterministic synthetic fixture generator for Blueprint apps and operations.
- [x] Add Tier 2 compile-time cacheability and invalidation-locality tests.
- [x] Add build/design-time telemetry capture and reporting.
- [x] Benchmark known runtime hotspots such as `BlueprintApp` registration and operation lookup.
- [x] Define PR-lane versus scheduled/manual benchmark lanes.
- [ ] Add stress/soak tests for concurrent dispatch, startup, and manifest/OpenAPI generation.

### Acceptance criteria

- [ ] Medium-scale compile-time locality is measured and gated in CI.
- [x] Build telemetry can explain cold, no-op, and single-edit behavior.
- [x] Runtime hotspot measurements exist for known registration/lookup bottlenecks.
- [x] A reviewable benchmark baseline exists for the known `BlueprintApp` hotspots before Phase 1 starts.
- [x] The fixture generator feeds both compile-time and runtime harnesses.
- [x] Tier 3 readiness has an explicit measurement plan before Signature365-scale migration starts.

### Tests and benchmarks

- [ ] Cacheability and invalidation-locality tests.
- [x] Build telemetry capture with `.binlog` and timing summaries.
- [x] BenchmarkDotNet or equivalent runtime benchmarks for startup, dispatch, and shell overhead.
- [ ] Stress tests for concurrent and high-volume paths.

### Docs and artifacts

- [x] Update `docs\observability-and-dev-experience.md` with measurement and artifact expectations if needed.
- [x] Keep benchmark artifact locations and lane expectations documented.
- [x] Publish benchmark/telemetry outputs in a reviewable format.

### Sub-agent guidance

- Use `general-purpose` for harness design.
- Use `task` for long-running builds/benchmarks.
- Use `code-review` to check whether measurements are representative and whether gates are deterministic.

### Durable progress

- completed first compile-time fixture slice: `src\Blueprint.SourceGenerator.Benchmarks\Scale\SyntheticBlueprintFixtureGenerator.cs` now emits a deterministic synthetic app plus ordered operation source files, and exposes a `CombinedSource` view so the same fixture can feed existing single-source generator harnesses while the broader multi-file scale harness is still landing
- completed proof and verification for that slice: `src\Blueprint.SourceGenerator.Tests\Scale\SyntheticBlueprintFixtureGeneratorTests.cs` now locks repeated-output stability, expected app/operation file emission, and descriptor-generator compilation for the synthetic fixture, and `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --no-restore` passes after the change
- next approved follow-on for this workstream: add a multi-source compile harness plus Tier 2 cacheability/invalidation-locality tests that mutate one generated operation and one shared-contract shape while keeping `SyntheticBlueprintFixtureGenerator` as the shared input surface
- completed first machine-readable invalidation-report alignment slice: `SyntheticFixtureInvalidationReport` now emits a deterministic `scale-invalidation` manifest envelope for the Tier 2 single-operation and shared-contract mutation scenarios, and `SyntheticFixtureInvalidationReportTests` snapshot those reports so locality evidence is reviewable as artifact data instead of only imperative assertions
- completed first build/design-time telemetry contract slice: `SyntheticBuildTelemetryReport` now emits a deterministic `build-telemetry` manifest envelope for cold-build and single-edit synthetic captures, and `SyntheticBuildTelemetryReportTests` snapshot generated-output counts, tracked stage counts, capture metadata, and optional mutation metadata so later `.binlog` runners have a reviewed contract to target
- completed first real synthetic MSBuild telemetry capture runner: `migration\performance-and-scale\Capture-SyntheticBuildTelemetry.ps1` now materializes a temporary synthetic app, emits cold-build, no-op-build, and single-edit-build `.binlog` plus `timing-summary.json` outputs, copies reviewable generated sources, and writes the reviewed `build-telemetry` envelope for each capture; `SyntheticBuildTelemetryRunnerTests.cs` now proves those artifacts end-to-end while keeping emitted inspection files out of `Compile` so repeat captures stay incremental
- completed first design-time telemetry follow-on: the same repo-native runner now emits a `design-time-build` capture through the reviewed `build-telemetry` envelope with `capture.isDesignTimeBuild = true`, giving Phase 0 a deterministic binlog-plus-summary seam for comparing IDE-style builds against the normal cold/no-op graph
- completed first concurrent executor-dispatch stress harness: `src\Blueprint.SourceGenerator.Tests.Integration\Generators\Pipelines\ConcurrentExecutorDispatchIntegrationTests.cs` now drives 64 start-gated operations through the generated `IntegrationTestBlueprintAppExecutor.ExecuteAsync(...)` shell, reports requested/completed/faulted counts plus returned payloads, and proves repeated concurrent dispatch stays stable on the same executor seam that the dispatch benchmark measures
- completed first repeated host startup and endpoint-mapping stress harness: `src\Blueprint.SourceGenerator.Tests\Artifacts\ManifestInspectionEndpointStressTests.cs` now rebuilds 40 development endpoint route builders, maps `MapBlueprintApi(...)`, and proves stable 5-endpoint counts plus 40/40 hosted manifest JSON route hits through a reusable `RepeatedEndpointMappingStressHarness`, keeping the first startup stress slice on the same host surface that `BlueprintEndpointMappingBenchmarks` measures
- completed first PR-lane versus scheduled/manual benchmark split: `BenchmarkLaneGuidanceTests.cs` now proves the two-lane performance model from `migration\performance-and-scale\plan.md` is surfaced in `docs\implementation-checklists.md`, `docs\observability-and-dev-experience.md`, and `.agents\skills\extending-blueprint\SKILL.md`, so deterministic stress proofs stay in the pull-request lane while noisier wall-clock or higher-volume comparisons move to scheduled/manual guidance
- completed first high-volume link-generation stress harness: `SyntheticBlueprintFixtureScaleHarnessTests.cs` now drives a 100-operation synthetic fixture with unique root links, proves descriptor output contains those links at scale, and requires a single root-link edit to change only `SyntheticOperation050_Descriptor.g.cs`; the same slice also fixes stale link-only descriptor output by removing the model-only combined-provider comparer from descriptor/app generators and by making `DescriptorPlan` equality include diagnostics plus metadata
- the breaking-changes/tooling workstream has now completed that analyzer/codemod/report branch, including the preserved sample-app migration review package for the `SimpleBlueprintApp` fixture.
- the reviewable performance-publication slice is now also complete: the scheduled/manual performance lane now has a preserved package that ties together the current BenchmarkDotNet markdown/HTML summaries, a fresh synthetic telemetry-runner capture, and representative `build-telemetry` / `scale-invalidation` snapshot evidence without inventing a new artifact family.
- the explicit Tier 3 readiness measurement-plan slice is now also complete: `migration\performance-and-scale\plan.md` now names the required Signature365-scale scenarios, scheduled/manual artifacts, comparison points, and review expectations so later broad migration claims have a durable published target.
- the first runtime benchmark-harness unification slice is now also complete: `Blueprint.SourceGenerator.Tests` and `Blueprint.SourceGenerator.Benchmarks` now share `SyntheticBlueprintFixtureGenerator`, and `BlueprintAppBenchmarkScenarioFactory` derives benchmark descriptor identities from the shared synthetic operation files instead of a benchmark-only naming scheme.
- the first concurrent executor-dispatch shared-fixture follow-on is now also complete: `ConcurrentExecutorDispatchStressHarness` now keeps the existing generated integration proof intact while also driving the benchmark executor path through a result-projection seam, so the repeated 64-dispatch runtime proof and `ExecutorDispatchBenchmarks` now share synthetic descriptor identities.
- the first repeated endpoint-mapping shared-fixture follow-on is now also complete: `ManifestInspectionEndpointStressTests.cs` now seeds a test-local `SharedSyntheticManifestInspectionBlueprintApp` from `BlueprintAppBenchmarkScenarioFactory.CreateDescriptors(10)`, so the repeated 40-iteration startup stress proof and `BlueprintEndpointMappingBenchmarks` now share synthetic descriptor identities without moving the hosted manifest resources out of the tests assembly that owns them.
- completed hosted-manifest route widening follow-on: `ManifestInspectionEndpointStressTests.cs` now requires the repeated shared-synthetic startup stress harness to record 40/40 hits for the hosted manifest JSON, markdown, schema, and diagnostics routes on one 40-iteration run, closing the remaining manifest-side startup stress gap without changing route names, manifest ownership, or the diagnostics page contract. The focused proof `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --filter "FullyQualifiedName~ManifestInspectionEndpointStressTests"` now passes after the harness records per-route hit counts instead of only the JSON route total.
- user-approved autonomous follow-on after the hosted-manifest-route stress widening slice: shift from Workstream 3 feature work into a cross-workstream Phase 0 closeout audit in this master plan. The next dependency-safe slice is to verify the Phase 0 exit-gate evidence, package the required review artifacts (manifest schema/example, synthetic fixture example, Tier 2 evidence, and hotspot benchmark baseline), and sync `migration\progress.json` for the formal Phase 0 exit review before any Phase 1 work begins.
- completed the cross-workstream Phase 0 technical closeout audit: the top-level Phase 0 exit-gate checklist now mirrors the already-landed foundation, migration-safety, manifest-contract, benchmark-baseline, and Tier 2 evidence, and `migration\phase0-review\artifact-index.json` now indexes the checked-in schema/example, the serialized `migration\phase0-review\synthetic-fixture-example.json` captured from `SyntheticBlueprintFixtureGenerator` output during the closeout audit, the reviewed Tier 2 snapshot evidence, and the reviewable `BlueprintApp` hotspot benchmark baselines. The remaining blocker before Phase 1 is the explicit `Phase 0 exit` human review gate.
- reconciled the stale orchestration pointer after that closeout audit: the previously expected Workstream 3 multi-source/Tier 2 harness slice was already complete, so `migration\progress.json` now points at `migration\operation-discovery-and-descriptors\plan.md` as the post-gate resume target. Phase 1 implementation remains blocked until the explicit `Phase 0 exit` human review gate closes or is explicitly waived.
- the `Phase 0 exit` human review gate is now closed by user approval, so the active implementation track moves into `migration\operation-discovery-and-descriptors\plan.md` for the first narrow Phase 1 slice.

## 4. Generated manifests and inspection surfaces

**Priority:** P0  
**Detailed plan:** `migration\generated-manifests-and-inspection-surfaces\plan.md`  
**Depends on:** foundation direction

### Checklist

- [x] Write and run a failing proof for the Phase 0 contract slice before implementation.
- [x] Deliver the Phase 0 contract first: envelope, versioning, ownership, naming, and contributor-collision rules.
- [x] Define a shared manifest envelope and naming/versioning contract.
- [x] Define how modules contribute without schema collisions.
- [ ] Make JSON the canonical representation and define projections to markdown/endpoints/tooling.
  - [x] Add the first build-time markdown projection and snapshot proof from `GeneratedManifestEnvelope`.
  - [x] Add runtime manifest loading and embedded resource projection as the first runtime inspection surface.
  - [x] Human-review the embedded-resource boundary before wider runtime endpoint or CLI projections begin.
  - [x] Add a development-only manifest JSON inspection endpoint that serves the loaded canonical manifest without recomputing discovery.
  - [x] Add a development-only manifest diagnostics page that consumes the hosted canonical JSON endpoint without introducing a runtime-side manifest model.
    - approved shape: `ManifestDiagnosticsPageEndpoint` maps `/.blueprint/manifests/operations.html` in development, fetches sibling `operations.json` client-side, and keeps raw fetch error details out of the page body
  - [ ] Add wider runtime inspection projection surfaces after the dev-only endpoint proves the envelope can be consumed through hosted inspection surfaces.
    - approved hosted follow-on: add a development-only markdown endpoint that serves prebuilt markdown bytes via an explicit resource-name seam, without projecting markdown inside `Blueprint.Runtime`
    - approved hosted follow-on after schema-validation review closeout: add a development-only schema endpoint that serves prebuilt schema bytes via an explicit resource-name seam, without teaching `Blueprint.Runtime` how to interpret or project schema semantics itself
    - human-approved hardening slice: reject conflicting debug-manifest entry ownership across multiple contributors with `BPMOD006` instead of silently allowing last-writer-wins replacement
- [x] Ensure runtime inspection projects from build-time data rather than recomputing.
- [ ] Align manifest output with migration reports, OpenAPI, and performance artifacts.
    - [x] approved first alignment slice after hosted schema review: emit a machine-readable invalidation report for the Tier 2 synthetic fixture harness using the shared manifest envelope, before broadening to benchmark/telemetry/OpenAPI alignment
    - [x] approved next alignment slice after the first performance artifact lands: keep the shared envelope and add documented build/design-time telemetry capture before broadening to benchmark-history publishing or OpenAPI alignment
    - approved next alignment slice after the telemetry contract lands: add a real synthetic MSBuild telemetry capture runner that emits `.binlog` and timing files into the reviewed `build-telemetry` envelope before broadening to benchmark-history publishing or OpenAPI alignment

### Acceptance criteria

- [x] The schema is stable enough to snapshot and publish.
- [x] Phase 0 can close with the shared contract agreed even if later modules still need to populate richer payloads incrementally.
- [x] At least one real module contributes manifest data through the shared contract.
- [x] Humans and AI agents can answer "what exists?" and "why?" without reverse-engineering generated code.
- [x] Runtime inspection does not invent or recompute conflicting data.

### Tests and benchmarks

- [x] Snapshot tests for manifest shape and content.
- [x] Runtime manifest loading tests covering embedded-resource projection, stable roundtripping, and no-rediscovery runtime reads.
- [x] Hosted manifest inspection endpoint tests covering the development-only JSON route, production dark-path gating, fail-fast resource validation during endpoint mapping, the review-ready diagnostics page shell, and generic DOM-facing error handling.
- [x] Schema validation tests.
  - completed first sub-slice: `GeneratedManifestEnvelope.FromJson(...)` now rejects malformed top-level `sections` and `operations` properties instead of silently ignoring non-object shapes
  - completed second sub-slice: `GeneratedManifestEnvelope.FromJson(...)` now requires `schemaVersion` to be a real JSON integer token instead of accepting a quoted numeric string
  - completed third sub-slice: `GeneratedManifestEnvelope.FromJson(...)` now requires required string fields to be real JSON string tokens instead of accepting numeric tokens through permissive conversions
  - completed fourth sub-slice: nested required-string failures now report full manifest paths for `app`, `generator`, and section contributor identities, and `GeneratedManifestEnvelopeTests` pin those invalid-token paths explicitly
  - completed fifth sub-slice: `GeneratedManifestEnvelope.schema.json` now publishes the approved envelope contract as a machine-readable artifact, `GeneratedManifestEnvelopeTests` validate the embedded operations manifest plus an invalid mutation against it, and `ManifestInspectionEndpointTests` pin the hosted markdown fixture semantically to the canonical JSON projection
- [x] Compatibility tests for multiple module contributors.
  - approved slice: `DebugManifestContributorCompatibilityTests` now proves the public `BuildDebugManifest(...)` seam rejects conflicting entry keys with `BPMOD006` while preserving non-conflicting composition
  - next recommended slice after this approval: add machine-readable schema validation for the approved manifest envelope and hosted fixture artifacts

### Docs and artifacts

- [x] Update `docs\observability-and-dev-experience.md`.
- [x] Update `docs\architecture.md` with manifest generation flow.
- [x] Publish schema/example artifacts in Phase 0 and richer reference-app artifacts incrementally later.

### Sub-agent guidance

- Use `explore` to compare current inspection surfaces with the target contract.
- Use `general-purpose` for schema and contributor-model work.
- Use `code-review` before stabilizing schema names or versioning.

## 5. Operation discovery and descriptors

**Priority:** P1  
**Detailed plan:** `migration\operation-discovery-and-descriptors\plan.md`  
**Depends on:** foundation module system

### Checklist

- [ ] Write and run a failing proof for the current slice before implementation.
- [ ] Move operation discovery fully to compile time.
- [ ] Make operation inclusion host-aware.
- [ ] Enrich descriptors with auth, result, resource, and feature metadata.
- [ ] Flatten `Results<T1, ...>` and related outcome contracts for downstream consumers.
- [ ] Resolve `[FromResource(...)]` into descriptor metadata.
- [ ] Move grouped-handler discovery (`ApiOperations` / `OperationsHandlerScanner`) into core Blueprint.
- [ ] Keep app registration generated from descriptors rather than runtime discovery.

### Acceptance criteria

- [ ] No runtime scanner is required to discover operations for the supported path.
- [ ] Descriptors carry enough metadata for HTTP, auth, validation, links, OpenAPI, and manifests.
- [ ] Grouped handlers work from core Blueprint, not only from Symprex consumer code.
- [ ] Missing or invalid operation metadata fails with actionable diagnostics.

### Tests and benchmarks

- [ ] Descriptor-generation snapshot tests.
- [ ] Diagnostics tests for invalid operations or metadata.
- [ ] Cacheability-sensitive tests for one-operation edits.
- [ ] Medium-scale discovery tests to guard invalidation breadth.

### Docs and artifacts

- [ ] Update `docs\architecture.md`.
- [ ] Update `docs\compile-time-validation.md` if new descriptor diagnostics land.
- [ ] Update migration tooling guidance if consumer references to grouped-handler types must be rewritten.

### Sub-agent guidance

- Use `explore` to map current discovery paths in both this repo and Signature365.
- Use `general-purpose` for descriptor-model implementation.
- Use `task` for snapshot and targeted test loops.

### Durable progress

- Completed first Phase 1 discovery-metadata slice: the existing `OperationDiscoveryManifestModule` now also contributes descriptor render steps that set `OperationDiscoveryFeatureData` on generated descriptors, reusing the approved `kind`/`namespace`/`typeName` facts already carried by the manifest path. Focused descriptor-generator tests and runtime-facing generated `Configure()` integration tests now pin that contract before broader host-aware discovery work.
- Completed first host-aware discovery slice: `LinkGenerationModule` now records shared HTTP host-exposure planning metadata whenever descriptor planning emits operation URLs, `OperationDiscoveryManifestModule` projects that into typed `OperationHostExposureFeatureData`, and `BlueprintApp` now indexes registered descriptors by host through `GetOperationsForHost(...)` without re-deriving URL ownership at runtime. Focused descriptor-generator assertions plus shared-app integration tests now prove HTTP-exposed operations are discoverable through that host index while internal-only operations stay absent there, and the verification matrix (`dotnet build .\Blueprint.sln --no-restore`, `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --no-restore`, `dotnet test .\src\Blueprint.SourceGenerator.CodeWriter.Tests\Blueprint.SourceGenerator.CodeWriter.Tests.csproj --no-restore`, and `dotnet test .\src\Blueprint.SourceGenerator.Tests.Integration\Blueprint.SourceGenerator.Tests.Integration.csproj --no-restore`) is green after the reviewed self-link snapshot update.
- Completed first manifest-host-alignment slice for this workstream: `DebugManifestPlanBuilder` now replays descriptor contributors before debug-manifest contributors, `DebugManifestContributorContext` exposes descriptor metadata through `GetMetadata<T>()`, and `OperationDiscoveryManifestModule` now projects ordered `hostExposure` arrays into the canonical `operations` manifest when compile-time host metadata exists. Focused module tests pin the explicit host-exposure assertion plus a reviewed manifest snapshot so inspection surfaces stay aligned with descriptor/app-registration metadata without re-deriving link attributes.
- Completed first enabled-host diagnostic slice for this workstream: `IEnabledHostContributor` now exposes the additive enabled-host registry seam, `LinkGenerationModule` is the first live HTTP host contributor, `HostExposureRegistration` preserves declaration locations for precise diagnostics without making them part of structural equality, and shared descriptor/debug-manifest planning now reports `BPOD001` when an operation records host exposure that no enabled host contributor supports. Focused module tests cover the unsupported-host failure case plus the cacheability-equality guard, the existing descriptor and `BlueprintApp` host-exposure proofs still pass, and the verification matrix (`dotnet format .\Blueprint.sln --no-restore --include ...`, `dotnet build .\Blueprint.sln --no-restore`, `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --no-restore`, `dotnet test .\src\Blueprint.SourceGenerator.CodeWriter.Tests\Blueprint.SourceGenerator.CodeWriter.Tests.csproj --no-restore`, and `dotnet test .\src\Blueprint.SourceGenerator.Tests.Integration\Blueprint.SourceGenerator.Tests.Integration.csproj --no-restore`) is green after the docs/skill updates.
- Next recommended follow-on for this workstream: once a second concrete host contributor exists, use the enabled-host registry to add the first conflicting-host exposure diagnostic instead of guessing conflict semantics from HTTP-only data.
- Reconciled the blocked conflicting-host follow-on: the repo still has only one concrete `IEnabledHostContributor`, so the dependency-safe prerequisite slice is now a minimal core-owned tasks host contributor based on the legacy `IBackgroundTask` marker. Keep that slice intentionally narrow: add only the host-specific marker and compile-time host-exposure metadata needed to unlock the later conflicting-host diagnostic, while leaving full background-task runtime/scheduler behavior in `migration\background-tasks\plan.md`.
- Completed the tasks-host prerequisite slice: `Blueprint.Tasks.IBackgroundTask` now provides the host-specific marker contract, `TasksHostModule` is the second concrete `IEnabledHostContributor`, generated descriptors/manifests now project `tasks` host exposure through the existing operation-discovery seam, and `BlueprintApp.GetOperationsForHost("tasks")` now proves the runtime host index can consume that metadata without background-task-specific rediscovery. The verification matrix (`dotnet format .\Blueprint.sln --no-restore --include ...`, `dotnet build .\Blueprint.sln --no-restore`, `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --no-restore`, `dotnet test .\src\Blueprint.SourceGenerator.CodeWriter.Tests\Blueprint.SourceGenerator.CodeWriter.Tests.csproj --no-restore`, and `dotnet test .\src\Blueprint.SourceGenerator.Tests.Integration\Blueprint.SourceGenerator.Tests.Integration.csproj --no-restore`) is green after the slice lands, so the next approved follow-on returns to the first conflicting-host diagnostic.
- Completed the previously approved conflicting-host follow-on: shared descriptor/debug-manifest planning now reports `BPOD002` when one operation records exposure to more than one enabled host contributor, keeping the enabled-host registry as the single compile-time source of truth for host ownership and failing before descriptor or manifest projection continues. Focused module and descriptor-generator tests pin the multi-host failure path while the existing host-ordering/cacheability guards still pass, and the verification matrix (`dotnet format .\Blueprint.sln --no-restore --include ...`, `dotnet build .\Blueprint.sln --no-restore`, `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --no-restore`, `dotnet test .\src\Blueprint.SourceGenerator.CodeWriter.Tests\Blueprint.SourceGenerator.CodeWriter.Tests.csproj --no-restore`, and `dotnet test .\src\Blueprint.SourceGenerator.Tests.Integration\Blueprint.SourceGenerator.Tests.Integration.csproj --no-restore`) is green after the reviewed docs/skill/tracking updates.
- Next approved Phase 1 handoff: move to the first pipeline-composition activation slice that routes `HandlerExecutionModule` through the activated seam while preserving the current result-wrapping behavior for that slice, and keep workstream 7 (`result-model-and-host-translation`) in the paired design loop so result-contract decisions do not freeze accidentally before pipeline, HTTP, error, or OpenAPI work.

## 6. Pipeline composition and execution

**Priority:** P1  
**Detailed plan:** `migration\pipeline-composition-and-execution\plan.md`  
**Depends on:** foundation + operation discovery + provisional result contract from workstream 7

Run this as a paired design loop with workstream 7. Do not freeze pipeline, HTTP, error, or OpenAPI behavior until the semantic result contract is agreed.

### Checklist

- [ ] Write and run a failing proof for the current slice before implementation.
- [ ] Replace the monolithic pipeline generator with modular stage contributions.
- [ ] Implement the shared executor shell for activity, logging, exception handling, and cleanup.
- [ ] Support handler discovery for inline handlers, external handlers, and grouped handlers.
- [ ] Define result normalization and post-execution contribution points.
- [ ] Close the re-query decision gate explicitly before resource work starts.

### Acceptance criteria

- [ ] A pipeline can be composed from ordered contributors without hard-coded feature interleaving.
- [ ] The shared shell owns the cross-cutting behavior consistently.
- [ ] At least one middleware-style module plugs into the new pipeline path cleanly.
- [ ] The re-query/nested-execution decision is documented and testable.

### Tests and benchmarks

- [ ] Snapshot tests for generated pipelines and executors.
- [ ] Integration tests proving handler invocation through the generated pipeline.
- [ ] Cacheability tests for pipeline-generation changes.
- [ ] Early runtime measurements for shell overhead.

### Docs and artifacts

- [ ] Update `docs\architecture.md` with the stage model and shell flow.
- [ ] Update `docs\observability-and-dev-experience.md` if tracing/logging hooks move.

### Sub-agent guidance

- Use a strong `general-purpose` agent for architecture and implementation.
- Use `task` for repeated build/test cycles.
- Require `code-review` before declaring the shared shell stable.

## 7. Result model and host translation

**Priority:** P1  
**Detailed plan:** `migration\result-model-and-host-translation\plan.md`  
**Depends on:** foundation + operation discovery + pipeline design loop with workstream 6

Run this as a paired design loop with workstream 6. The result contract must be locked before pipeline, HTTP, error, or OpenAPI contracts are treated as stable.

### Checklist

- [ ] Write and run a failing proof for the current slice before implementation.
- [ ] Define host-agnostic semantic result types and metadata.
- [ ] Define attachable host hints such as ETag/cache-control without host leakage into core result types.
- [ ] Align flattened result metadata with descriptors and future OpenAPI.
- [ ] Define host translation seams for HTTP and later non-HTTP hosts.

### Acceptance criteria

- [ ] Pipelines return semantic results rather than host-specific outcomes.
- [ ] HTTP translation is isolated and testable.
- [ ] Descriptor, result, and OpenAPI models agree on the flattened outcome contract.
- [ ] The public result story is coherent enough for sample code and future package authors.

### Tests and benchmarks

- [ ] Unit tests for result translation and union handling.
- [ ] Snapshot tests for generated result metadata.
- [ ] Integration tests for success and not-found/conflict style paths.

### Docs and artifacts

- [ ] Update `docs\architecture.md`.
- [ ] Update any sample/reference documentation that exposes the preferred result contract.

### Sub-agent guidance

- Use `general-purpose` for the contract design.
- Use `task` for integration verification.
- Use `code-review` if result changes affect public API shapes.

### Durable progress

- Live active/next/last/gate tracking for this workstream lives only in `migration\progress.json`. This section records durable slice approvals and rationale; it is not the live tracker.
- The paired workstream-6 activation baseline is already in the repository: handler execution is routed through the contributor seam and validation already participates as a production contributor, so the next dependency-safe result-model slice was to introduce the first public host-agnostic `Results<T1, T2>` contract surface in `Blueprint.Model`.
- Completed the first result-model foundation slice:
  - `Blueprint.Model.Results<T1, T2>` now exists as the first public host-agnostic semantic-union contract.
  - Focused model tests now cover implicit conversions plus wrong-member/default-state failures.
  - `using-blueprint` now points consumers at the new contract surface.
  - The verification matrix (`dotnet format .\Blueprint.sln --no-restore --include src\Blueprint.Runtime.Model\Results.cs src\Blueprint.SourceGenerator.Tests\Model\ResultsTests.cs`, `dotnet build .\Blueprint.sln --no-restore`, `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --no-restore`, `dotnet test .\src\Blueprint.SourceGenerator.Tests.Integration\Blueprint.SourceGenerator.Tests.Integration.csproj --no-restore`, and `dotnet test .\src\Blueprint.SourceGenerator.CodeWriter.Tests\Blueprint.SourceGenerator.CodeWriter.Tests.csproj --no-restore`) is green after repairing the stale orchestration pointer that still targeted already-landed pipeline work.
- Completed the handler-return normalization slice:
  - `HandlerExecutionModule` now normalizes plain non-null handler values into typed `Blueprint.Runtime.Exec.OkResult<T>`, `void`/`Task` into `Blueprint.Runtime.Exec.NoResultOperationResult`, explicit `Blueprint.OperationResult` returns through unchanged, and the first `Results<T1, T2>` contract into direct concrete-member returns.
  - A focused follow-up keeps explicit `Blueprint.Runtime.Exec.OkResult<T>` handlers compatible with matching plain declared contracts such as `IQuery<T>` / `ICommand<T>` so consumers can opt into explicit success semantics without widening the declared contract shape.
  - Pipeline diagnostics `BP105`-`BP107` now reject nullable plain-value returns, unsupported union members, and handler returns that do not match the declared operation result contract.
  - The proof set covers generator snapshots, compile-time diagnostics, runtime execution in `src\Blueprint.SourceGenerator.Tests.Integration\Generators\Pipelines\HandlerReturnNormalizationIntegrationTests.cs`, and a green verification matrix across `dotnet format`, `Blueprint.SourceGenerator.Tests`, `Blueprint.SourceGenerator.CodeWriter.Tests`, `Blueprint.SourceGenerator.Tests.Integration`, and `dotnet build .\Blueprint.sln`.
- Completed the flattened descriptor outcomes slice:
  - `FlattenedResultOutcomeModule` now projects declared semantic result contracts into additive `OperationResultOutcomesFeatureData`, flattening `Results<T1, T2>` into ordered `OperationResultOutcome` entries and surfacing single declared semantic result contracts as one-outcome descriptor metadata.
  - Contract-only compilations that do not reference `Blueprint.OperationResult` now skip that descriptor feature instead of failing generator execution, so descriptor generation stays safe in model-only consumer projects such as `Blueprint.SourceGenerator.Sample.Contract`.
  - The proof set now covers descriptor-source assertions, runtime `Configure().GetFeatureData<OperationResultOutcomesFeatureData>()` checks, the contract-only regression test, and a green verification matrix across `dotnet format`, `Blueprint.SourceGenerator.Tests`, `Blueprint.SourceGenerator.CodeWriter.Tests`, `Blueprint.SourceGenerator.Tests.Integration`, and `dotnet build .\Blueprint.sln`.
- Completed the first runtime-result migration slice:
  - `MapBlueprintApi(...)` now routes supported success shapes through `Blueprint.Runtime.Http.HttpOperationResultExecutor`, translating `OkResult<T>` payloads to JSON `200 OK` responses and `NoResultOperationResult` to empty `200 OK` responses without relying on `OkResult.ExecuteAsync(...)`.
  - Unsupported legacy results still fall back to direct `OperationResult.ExecuteAsync(...)`, so the new host-owned executor seam is an explicit migration path rather than an accidental wholesale replacement of the old contract.
  - The proof set now includes host-surface endpoint tests for translated `OkResult<T>`, translated `NoResultOperationResult`, and the legacy fallback boundary, plus a green `Blueprint.SourceGenerator.Tests` project run.
- Completed the first host-executor availability diagnostic slice:
  - `HostExecutorAvailabilityModule` now runs during descriptor generation, reuses the flattened semantic-result helper surface, and reports `BP108` when an HTTP-exposed operation declares a semantic result shape that the current enabled HTTP executor path does not translate.
  - Supported `OkResult<T>` and `NoResultOperationResult` contracts stay clean, while mixed `Results<T1, T2>` unions report only the unsupported semantic-result members.
  - The proof set now includes a focused red-to-green descriptor diagnostic test file (`OperationDescriptorSourceGeneratorTests.HostExecutorAvailability.cs`) plus a green `Blueprint.SourceGenerator.Tests` project run.
- Completed the first ambiguous host-translation diagnostic slice:
  - `AmbiguousHostTranslationModule` now runs after the host-executor availability check and reports `BP109` when an HTTP-exposed `Results<T1, T2>` contract contains multiple supported semantic-result members that still collapse to the same current HTTP response shape.
  - The first approved ambiguity rule stays intentionally narrow: it targets current same-shape HTTP success unions such as multiple `OkResult<T>` members that all translate to `200` JSON responses, while unsupported members remain owned by `BP108`.
  - The proof set now includes a focused red-to-green descriptor diagnostic test file (`OperationDescriptorSourceGeneratorTests.AmbiguousHostTranslation.cs`) covering the ambiguous same-shape union, a clean single-result regression, the BP108/BP109 boundary, and a green `Blueprint.SourceGenerator.Tests` project run.
- Completed the first HTTP union discrimination diagnostic slice:
  - `HttpUnionDiscriminationStrategyModule` now runs after the host-translation ambiguity check and reports `BP110` when an HTTP-exposed `Results<T1, T2>` contract keeps the same current HTTP status code across supported members without an explicit host/client discrimination strategy.
  - The first approved discrimination rule stays intentionally narrow: it targets same-status mixed-shape unions such as `OkResult<T>` plus `NoResultOperationResult`, while unsupported members remain owned by `BP108` and same-shape collisions remain owned by `BP109`.
  - The proof set now includes a red-to-green descriptor diagnostic test file (`OperationDescriptorSourceGeneratorTests.HttpUnionDiscrimination.cs`) covering the same-status mixed-shape union plus the `BP108`/`BP109` boundaries, a green `Blueprint.SourceGenerator.Tests` project run, and a green `dotnet build .\Blueprint.sln --no-restore` verification pass.
- Completed the first flattened descriptor outcome-alignment diagnostic slice:
  - `FlattenedResultOutcomeModule` now reports `BP111` before descriptor emission when a `Results<T1, T2>` contract would force `OperationResultOutcomesFeatureData` away from the declared semantic member set, such as plain CLR payload members or nested union members.
  - The descriptor and pipeline seams now share the same union-member validity predicate, so unsupported `Results<T1, T2>` members no longer pass descriptor projection while only failing later in handler validation.
  - The proof set now includes a red-to-green descriptor diagnostic test in `OperationDescriptorSourceGeneratorTests.FlattenedOutcomes.cs`, a green `Blueprint.SourceGenerator.Tests` project run (`244` tests), a green targeted integration check for `AppDataModelSourceGeneratorResultOutcomesFeatureDataTests`, and a green `dotnet build .\Blueprint.sln --no-restore` verification pass.
- Repaired the queued no-result-shape-separation follow-on:
  - The previously queued diagnostic depends on an unresolved result-model question in the workstream plan (`What is the default HTTP status mapping for host-neutral NoResult, and how should that interact with explicit host-specific no-content/status-only result families?`) and the current repo still exposes only host-neutral `NoResultOperationResult`, not a concrete host-specific no-content/status-only result family to prove against.
  - That leaves the diagnostic slice without executable acceptance criteria today, so the next ready slice must first lock the default HTTP status mapping plus the ownership model for host-specific result families.
- Completed the default HTTP status mapping and host-specific result-family ownership slice:
  - `Blueprint.Runtime.Exec.NoResultOperationResult` remains the host-neutral success shape and is now explicitly locked to the default empty HTTP `200 OK` translation path.
  - `Blueprint.Runtime.Http.HttpNoContentResult` is now the first concrete host-owned no-content result family member, translating to empty HTTP `204 No Content` through `HttpOperationResultExecutor` rather than widening the core taxonomy.
  - The proof set now includes a red-to-green descriptor support test in `OperationDescriptorSourceGeneratorTests.HostExecutorAvailability.cs`, a red-to-green host translation test in `OperationResultTranslationEndpointTests.cs`, and a green targeted `Blueprint.SourceGenerator.Tests` run for those new seams.
- Completed the first no-result-shape-separation diagnostic slice:
  - `NoResultShapeSeparationModule` now reports `BP112` when HTTP-visible `Results<T1, T2>` contracts mix host-neutral `NoResultOperationResult` with host-owned no-content/status-only result families such as `Blueprint.Runtime.Http.HttpNoContentResult`, preserving the semantic distinction before descriptor flattening or client generation can blur it.
  - The first approved rule stays intentionally narrow: unsupported members remain owned by `BP108`, same-shape collisions remain owned by `BP109`, and same-status body/no-body unions remain owned by `BP110`.
  - The proof set now includes the new red-to-green descriptor test file `OperationDescriptorSourceGeneratorTests.NoResultShapeSeparation.cs` covering both member orders, clean single-result regressions, and explicit `BP108`/`BP110` boundaries.
- Completed the first key-only resource-event HTTP mapping slice:
  - `Blueprint.Runtime.Exec.KeyOnlyResourceEventResult` now provides the first host-neutral key-only resource-event semantic shape, carrying stable event/resource identity while staying distinct from both host-neutral `NoResultOperationResult` and host-owned HTTP no-content conveniences.
  - The current HTTP host now translates that semantic result to empty `201 Created` responses by convention through `HttpOperationResultExecutor`, and the shared response-shape helper threads the same mapping through descriptor availability and ambiguity checks.
  - The proof set now includes a red-to-green descriptor support test, a red-to-green endpoint translation test, a flattened descriptor-outcome proof, and a same-status boundary test proving `Results<KeyOnlyResourceEventResult, NoResultOperationResult>` stays clear of `BP110`.
- Approved the first semantic result-family cut for this workstream:
  - Human direction now keeps `CreatedResult<T>`, `AcceptedResult` / `AcceptedResult<T>`, and `ConflictResult` in the first implementation pass instead of deferring any of them to a later phase.
  - Even with that broader pass-1 scope, the implementation batch still lands one family at a time; `CreatedResult<T>` is the next executable slice because it extends the newly locked `201 Created` success path without pulling in accepted/conflict semantics at the same time.
- Completed the first body-bearing created-result slice:
  - `Blueprint.Runtime.Exec.CreatedResult` / `CreatedResult<T>` now provide the first host-neutral created-with-payload semantic success family, and the current HTTP host maps it to JSON `201 Created` responses without collapsing it into either `OkResult<T>` (`200`) or the empty-body key-only event path.
  - The descriptor-time result helpers now treat that family as part of the supported HTTP executor surface and keep its `201` body-bearing response shape distinct in both flattened-outcome validation and host-translation ambiguity checks.
  - The proof set now includes a red-to-green endpoint translation test, a red-to-green descriptor support test, a flattened descriptor-outcome proof, a clean same-status boundary proving `Results<CreatedResult<string>, OkResult<string>>` stays clear of `BP109` and `BP110`, and a new regression test proving `Results<CreatedResult<string>, KeyOnlyResourceEventResult>` still belongs to the intentional `BP110` same-status guard.
- Completed the Accepted-result asynchronous success slice:
  - `Blueprint.Runtime.Exec.AcceptedResult` / `AcceptedResult<T>` now provide the next host-neutral "accepted but not yet completed" semantic success family, and the current HTTP host maps it to JSON `202 Accepted` responses without collapsing it into either immediate `OkResult<T>` success (`200`) or body-bearing created-resource success (`201`).
  - The descriptor-time result helpers now treat that family as part of the supported HTTP executor surface and keep its `202` body-bearing response shape distinct in both flattened-outcome validation and host-translation ambiguity checks.
  - The proof set now includes a red-to-green endpoint translation test, a red-to-green descriptor support test, a flattened descriptor-outcome proof, and a clean status boundary proving `Results<AcceptedResult<string>, OkResult<string>>` stays clear of `BP109` and `BP110`.
- Completed the Conflict-result semantic conflict slice:
  - `Blueprint.Runtime.Exec.ConflictResult` / `ConflictResult<T>` now provide the final host-neutral semantic family in the currently approved first result-family cut, and the current HTTP host maps it to JSON `409 Conflict` responses without collapsing it into any of the `200`, `201`, or `202` success paths.
  - The descriptor-time result helpers now treat that family as part of the supported HTTP executor surface and keep its `409` body-bearing response shape distinct in both flattened-outcome validation and host-translation ambiguity checks.
  - The proof set now includes a red-to-green endpoint translation test, a red-to-green descriptor support test, a flattened descriptor-outcome proof, and a clean status boundary proving `Results<ConflictResult<string>, OkResult<string>>` stays clear of `BP109` and `BP110`.
- Review gate after the first semantic result-family cut:
  - The explicitly approved family cut for Workstream 7 is now complete: `CreatedResult<T>`, `AcceptedResult` / `AcceptedResult<T>`, and `ConflictResult` are all landed.
  - No explicit next slice is approved in `migration\plan.md` or `migration\result-model-and-host-translation\plan.md` after Conflict, so the live tracker should stop with `nextReadySlice = null` and open a human-direction gate instead of guessing between more result-model work and a pivot into the paired error/HTTP workstreams.
- Cleared the post-Conflict review gate by later human direction:
  - The human explicitly delegated next-slice selection to this session, so the next approved follow-on is `NotFoundResult` / `NotFoundResult<T>` as the next semantic missing-resource result family with default HTTP `404 Not Found` mapping.
  - That choice stays in Workstream 7 because `NotFoundResult` is already part of the canonical shared semantic taxonomy and extends the same proven distinct-status host-translation pattern without forcing Workstream 8 to invent or borrow a missing-resource failure shape first.
- Completed the NotFound-result semantic missing-resource slice:
  - `Blueprint.Runtime.Exec.NotFoundResult` / `NotFoundResult<T>` now provide the next host-neutral semantic family for missing-resource outcomes, and the current HTTP host maps that family to body-bearing JSON `404 Not Found` responses without collapsing it into either success or conflict semantics.
  - The descriptor-time result helpers now treat that family as part of the supported HTTP executor surface and keep its `404` body-bearing response shape aligned across host-availability, flattened-outcome, ambiguity, and same-status discrimination checks.
  - The proof set now includes a red-to-green endpoint translation test, a red-to-green descriptor support test, a flattened descriptor-outcome proof, and a clean status boundary proving `Results<NotFoundResult<string>, OkResult<string>>` stays clear of `BP109` and `BP110`.
- Review gate after the NotFound-result slice:
  - `NotFoundResult` is now landed, but no explicit post-NotFound follow-on is recorded in the durable plans yet.
  - If execution stops here, the live tracker should reopen a human-direction gate rather than guessing between more result-model taxonomy work and a pivot into Workstream 8.
- Cleared the post-NotFound review gate by later session judgment:
  - The next approved follow-on stays in Workstream 7: introduce canonical `ValidationFailedResult` while retaining `ValidationFailedOperationResult` as a compatibility alias or shim.
  - This is narrower and better grounded than a Workstream 8 pivot because the result-model plan explicitly names `ValidationFailedResult` as part of the canonical taxonomy and explicitly permits legacy `ValidationFailedOperationResult`-style aliases while the broader error-model plan still depends on a stable result contract.
- Completed the ValidationFailed-result canonicalization slice:
  - `Blueprint.Runtime.Exec.ValidationFailedResult` is now the canonical semantic validation-failure result, while `Blueprint.Middleware.ValidationFailedOperationResult` remains available as a compatibility alias inheriting from it.
  - `ValidationModule` and `PipelineCodeGenerator` now emit canonical validation-failure result/helper names for generated short-circuits and validation-exception catch translation, while host-visible contracts using validation-failure results still intentionally report `BP108`.
  - The proof matrix now includes a red-to-green generated validation pipeline proof, a red-to-green canonical `BP108` boundary proof, a green `Blueprint.SourceGenerator.Tests` project run (`275` tests), a green `Blueprint.SourceGenerator.Tests.Integration` run (`55` tests), and a green `dotnet build .\Blueprint.sln --no-restore`.
- Next approved follow-on after ValidationFailedResult canonicalization:
  - Stay in Workstream 7 long enough to introduce an internal canonical `UnhandledExceptionResult` shape while retaining `UnhandledExceptionOperationResult` as the compatibility/internal legacy name.
  - That choice closes the remaining shared result-taxonomy gap recorded in the workstream plan and keeps Workstream 8 (`error-model-and-exception-handling`) from having to invent failure-result taxonomy before the result model is explicitly locked.
- Completed the UnhandledExceptionResult canonicalization slice:
  - `Blueprint.Runtime.Exec.UnhandledExceptionResult` is now the canonical internal fallback result, while `Blueprint.UnhandledExceptionOperationResult` remains available only as a compatibility alias inheriting from it.
  - The shared outer exception shell now emits the canonical name, while descriptor validation keeps both direct and `Results<T1, T2>` contracts that reference either unhandled-exception name on the `BP111` side of the public semantic-contract boundary.
  - The proof matrix now includes a red-to-green generated pipeline-source proof, direct and union `BP111` boundary proofs for canonical and legacy names, a runtime integration proof for thrown handlers, a green `Blueprint.SourceGenerator.Tests` run (`280` tests), a green `Blueprint.SourceGenerator.Tests.Integration` run (`56` tests), a green solution build, and clean independent review.
- Next approved follow-on after UnhandledExceptionResult canonicalization:
  - Pivot into Workstream 8 with a narrow first slice: extract a minimal exception-translator seam around the shared executor shell while preserving the current `ApiException` handling and the generic unhandled fallback.
  - That is the safest next step because the result-model taxonomy is now locked enough for failure work, and the error-model plan explicitly starts with shared executor-shell exception boundaries and pipeline exception translators before host-specific failure documentation or OpenAPI work.
- Completed the first Workstream 8 exception-translator seam slice:
  - `PipelineCodeGenerator` now delegates outer catch-shell emission to `IPipelineExceptionTranslator`, and `DefaultPipelineExceptionTranslator` preserves the current validation-exception handling, non-5xx `ApiException` logging path, and generic unhandled fallback behavior.
  - The proof matrix now includes a red-to-green direct codegen proof that a custom translator can replace the default shell, unchanged generator behavior locks for canonical validation and unhandled results, a green runtime thrown-handler integration proof, green `dotnet format .\Blueprint.sln --no-restore`, a green `dotnet build .\Blueprint.sln --no-restore`, and green `Blueprint.SourceGenerator.Tests` (`281`), `Blueprint.SourceGenerator.CodeWriter.Tests` (`45`), and `Blueprint.SourceGenerator.Tests.Integration` (`56`) runs.
- Autonomous next Workstream 8 follow-on after seam extraction:
  - Stay in Workstream 8 and implement ordered exception translator policy with compile-time conflict detection.
  - That is the narrowest dependency-safe next slice because the seam extraction made exception translation replaceable, but it did not yet define how multiple translators compose or conflict; that ordering policy is a prerequisite for later descriptor-level failure metadata, host-aware mapping, and package-provided translator work.
  - The live tracker should therefore keep Workstream 8 active, set `nextReadySlice` to the ordered-translator-policy slice, and continue without reopening a human-direction gate unless a real blocker or explicit review gate appears.
- Completed the ordered exception-translator policy slice:
  - `PipelineExceptionTranslatorPolicy.CreateDefault()` now validates and orders the default executor-shell translators before pipeline generation continues, keeping the two validation translators explicit and the catch-all fallback last.
  - `BP113` now rejects overlapping non-fallback translator ownership, so exception translation can no longer drift based on incidental translator registration order.
  - The proof matrix now includes red-to-green policy ordering/conflict tests, a direct codegen catch-order proof, targeted integration proofs for `ApiException` plus validation-exception paths, green `dotnet format .\Blueprint.sln --no-restore`, a green `dotnet build .\Blueprint.sln --no-restore`, and green `Blueprint.SourceGenerator.Tests` (`285`), `Blueprint.SourceGenerator.CodeWriter.Tests` (`45`), and `Blueprint.SourceGenerator.Tests.Integration` (`58`) runs.
- Autonomous next Workstream 8 follow-on after ordered translator policy:
  - Stay in Workstream 8 and introduce descriptor-level failure metadata model aligned with ordered translator ownership.
  - That is the narrowest dependency-safe next slice because translator precedence and overlap are now explicit, which lets descriptor generation project stable failure facts before host-aware mapping or package-provided translator activation widens the contract.
  - The live tracker should therefore keep Workstream 8 active, set `nextReadySlice` to the descriptor-level-failure-metadata slice, and continue without reopening a human-direction gate unless a real blocker or explicit review gate appears.
- Completed the descriptor-level failure metadata slice:
  - `FailureOutcomesModule` now projects ordered `OperationFailureOutcomesFeatureData` onto generated descriptors using the validated default translator policy, so failure ownership is available from descriptor feature data instead of only from generated catch blocks.
  - `OperationFailureOutcome` currently keeps the translator id, handled exception type name, and translated result type name as strings, which preserves contract-only descriptor generation while deferring any runtime-type or host-mapping freeze to the next slice.
  - The proof matrix now includes red-to-green descriptor-generator assertions for emitted failure metadata, a contract-only descriptor proof that the feature data survives without `Blueprint.OperationResult`, a green runtime `Configure()` integration proof for ordered failure metadata lookup, and the broader verification sweep captured with this slice before widening the contract again.
- Autonomous next Workstream 8 follow-on after descriptor-level failure metadata:
  - Stay in Workstream 8 and introduce host-aware mapping from descriptor failure metadata to HTTP status and response-body conventions.
  - That is the narrowest dependency-safe next slice because descriptor generation now exposes stable failure facts, which lets the HTTP host translate known failure families from one compile-time source of truth before package-provided translators or OpenAPI projection broaden the surface area.
  - The live tracker should therefore keep Workstream 8 active, set `nextReadySlice` to the host-aware-failure-mapping slice, and continue without reopening a human-direction gate unless a real blocker or explicit review gate appears.

## 8. Error model and exception handling

**Priority:** P1  
**Detailed plan:** `migration\error-model-and-exception-handling\plan.md`  
**Depends on:** foundation + operation discovery + pipeline composition + result model

### Checklist

- [ ] Write and run a failing proof for the current slice before implementation.
- [ ] Define explicit success/failure contracts.
- [ ] Define ordered exception translators.
- [ ] Define how failure metadata feeds result translation and documentation.
- [ ] Integrate exception handling with the shared executor shell.

### Acceptance criteria

- [ ] Expected exceptions translate deterministically to semantic failures.
- [ ] Unexpected exceptions still flow through consistent shell-owned logging/telemetry.
- [ ] Failure contracts are visible to hosts and OpenAPI generation.

### Tests and benchmarks

- [ ] Unit tests for translator ordering and fallback behavior.
- [ ] Integration tests for representative failure paths.
- [ ] Snapshot coverage if generated error metadata or executor code changes.

### Docs and artifacts

- [ ] Update `docs\architecture.md` if the failure model becomes part of the public design story.
- [ ] Update `docs\observability-and-dev-experience.md` for error telemetry expectations.

### Sub-agent guidance

- Use `general-purpose` for design and implementation.
- Use `code-review` to look for swallowed exceptions, inconsistent failure semantics, or host leakage.

## 9. HTTP host and message population

**Priority:** P1  
**Detailed plan:** `migration\http-host-and-message-population\plan.md`  
**Depends on:** foundation + operation discovery + pipeline composition + result model

### Checklist

- [ ] Write and run a failing proof for the current slice before implementation.
- [ ] Generate HTTP-visible operation metadata at compile time.
- [ ] Generate parameter-binding/message-population plans.
- [ ] Generate result execution plans for supported semantic result shapes.
- [ ] Align with `System.Text.Json` source generation and serializer-context validation.
- [ ] Remove runtime source iteration for supported binding scenarios.

### Acceptance criteria

- [ ] The generated HTTP host can execute real sample operations end to end.
- [ ] Binding and result execution are driven from generated plans, not runtime reflection loops.
- [ ] Missing serializer/binding requirements fail at build time with good diagnostics.
- [ ] Routing, binding, and result translation are snapshot- and integration-testable.

### Tests and benchmarks

- [ ] Integration tests for route execution, binding, and result handling.
- [ ] Snapshot tests for generated host/binder/result-executor output.
- [ ] Cacheability tests for HTTP plan changes.
- [ ] Smoke allocation/startup checks for mapped endpoints.

### Docs and artifacts

- [ ] Update `docs\architecture.md`.
- [ ] Update `docs\compile-time-validation.md` with HTTP binding/serializer diagnostics.
- [ ] Publish reference-app route and host manifests once stable.

### Sub-agent guidance

- Use `general-purpose` for host/binding implementation.
- Use `task` for integration tests and smoke benchmarks.
- Bring in `dotnet-testing` guidance for HTTP integration coverage.

## 10. Validation

**Priority:** P1  
**Detailed plan:** `migration\validation\plan.md`  
**Depends on:** foundation + operation discovery + pipeline composition

### Checklist

- [ ] Write and run a failing proof for the current slice before implementation.
- [ ] Move validation toward source-generated, DataAnnotations-first plans.
- [ ] Preserve only the strategic Blueprint-specific validators called out in the plan.
- [ ] Provide compatibility aliases where they reduce friction and remain clear.
- [ ] Emit semantic validation-failure results rather than ad-hoc runtime behavior.
- [ ] Add a registration seam for package-provided validation sources if needed.

### Acceptance criteria

- [ ] Supported validators generate deterministic validation plans.
- [ ] Unsupported or deprecated validator shapes fail with clear migration guidance.
- [ ] Validation failures surface through the shared result/error story.
- [ ] Validation work remains compatible with compile-time diagnostics and manifests.

### Tests and benchmarks

- [ ] Unit tests for validator planning and execution.
- [ ] Diagnostics tests for unsupported validator usage.
- [ ] Integration tests for invalid input paths.
- [ ] Snapshot coverage for validation plans where useful.

### Docs and artifacts

- [ ] Update `docs\compile-time-validation.md`.
- [ ] Update `docs\implementation-checklists.md` if validation workflow expectations change.

### Sub-agent guidance

- Use `general-purpose` for generator/runtime implementation.
- Use `task` or testing-focused help for the validator matrix and failure-path tests.

## 11. Authentication and authorisation

**Priority:** P1  
**Detailed plan:** `migration\authentication-and-authorisation\plan.md`  
**Depends on:** foundation + operation discovery + pipeline composition + HTTP host

### Checklist

- [ ] Write and run a failing proof for the current slice before implementation.
- [ ] Generate explicit `AuthorizationDescriptor` metadata per operation.
- [ ] Support JWT scope checks and resource-key claim matching as separate generated paths.
- [ ] Generate `{Auth:...}` and property-template expansion from compile-time metadata.
- [ ] Preserve the important public attributes and semantics.
- [ ] Provide seams for user-context loading and consumer-owned ownership/resource resolvers.

### Acceptance criteria

- [ ] Auth checks are explicit, inspectable, and testable per operation.
- [ ] Descriptor/manifests explain auth provenance and requirements.
- [ ] Core Blueprint owns the compile-time auth contract; consumer packages own role/ownership models that cannot be generalized.
- [ ] The implementation does not hide behavior inside magic runtime expansion rules.

### Tests and benchmarks

- [ ] Unit tests for auth-descriptor generation and permission-expansion logic.
- [ ] Integration tests for allow-anonymous, authenticated, authorized, and denied paths.
- [ ] Snapshot/manifests for auth metadata.
- [ ] Runtime smoke checks for auth overhead once the path exists.

### Docs and artifacts

- [ ] Update `docs\architecture.md`.
- [ ] Update `docs\observability-and-dev-experience.md` if access metadata becomes inspectable.
- [ ] Update `docs\compile-time-validation.md` with auth-related diagnostics.

### Sub-agent guidance

- Use `explore` to inventory legacy Signature365 auth/resource patterns before coding.
- Use `general-purpose` for the descriptor/check generation path.
- Require `code-review` for any security-sensitive behavior or public attribute compatibility decision.

## 12. Auditing and observability

**Priority:** P1  
**Detailed plan:** `migration\auditing-and-observability\plan.md`  
**Depends on:** foundation + shared shell design; starts during foundation/pipeline work and evolves with the Phase 1 modules

### Checklist

- [ ] Write and run a failing proof for the current slice before implementation.
- [ ] Add shared tracing/activity conventions through the executor shell.
- [ ] Add structured logging using the preferred generated/runtime approach.
- [ ] Define generated inspection surfaces for modules, stages, auth, and execution plans.
- [ ] Keep the outputs OTLP/OpenTelemetry-friendly.

### Acceptance criteria

- [ ] Every generated operation participates in consistent tracing/logging hooks.
- [ ] Inspection surfaces explain the registered API and generated execution story.
- [ ] Observability work is designed into the shared shell rather than bolted on afterward.

### Tests and benchmarks

- [ ] Integration tests or assertions around emitted metadata/logging hooks where feasible.
- [ ] Snapshot tests for manifests/inspection output.
- [ ] Runtime smoke verification that the shared shell keeps observability overhead predictable.

### Docs and artifacts

- [ ] Update `docs\observability-and-dev-experience.md`.
- [ ] Update `docs\architecture.md` if shell-owned observability changes the core flow.

### Sub-agent guidance

- Use `general-purpose` for integration across shell, manifests, and runtime.
- Use `code-review` to look for inconsistent logging, missing correlation metadata, or duplicated shell work.

## 13. Reference app and end-to-end example

**Priority:** P1  
**Detailed plan:** `migration\reference-app-and-end-to-end-example\plan.md`  
**Depends on:** Layer 1 depends on pipeline composition + HTTP host; the fuller app evolves with later modules

| Layer | Purpose | Depends on | Gate |
| --- | --- | --- | --- |
| Layer 1 | Runnable generated HTTP sample with real handlers and deterministic seeded data | Pipeline composition + HTTP host | Required for Phase 1 exit |
| Layer 2 | Validation, auth, links, richer results, manifests, and inspection surfaces in one coherent app | Phase 1 core modules | Required before broader consumer migration confidence |
| Layer 3 | OpenAPI, resource/post-processing examples, and any carefully chosen extension examples | Phase 2 modules | Required for Phase 2 exit |

### Checklist

- [ ] Write and run a failing proof for the current slice before implementation.
- [ ] Replace `NotImplementedException` handlers with deterministic in-memory behavior.
- [ ] Make the sample runnable end to end with the generated HTTP path.
- [ ] Grow the sample into a curated 10-20 operation app in the templates domain.
- [ ] Add validation, auth, link, result, and OpenAPI examples in deliberate layers.
- [ ] Add snapshot-tested manifests and integration tests.
- [ ] Add a sample-specific README once the app is genuinely useful.

### Acceptance criteria

- [ ] The sample demonstrates the preferred future consumer story, not only generator internals.
- [ ] The app remains infrastructure-light and deterministic.
- [ ] Layer 1 is complete before claiming HTTP parity.
- [ ] Later layers prove validation, auth, links, observability, and OpenAPI in one coherent app.

### Tests and benchmarks

- [ ] End-to-end integration tests for happy and key failure paths.
- [ ] Snapshot tests for routes, links, manifests, and OpenAPI as those land.
- [ ] Smoke tests for preferred host wiring and app startup.

### Docs and artifacts

- [ ] Add/update the sample README.
- [ ] Publish sample manifests and OpenAPI artifacts.
- [ ] Optionally add a small before/after migration note once migration tooling exists.

### Sub-agent guidance

- Use `general-purpose` for consumer-facing sample design and implementation.
- Use `task` for integration test runs.
- Use `code-review` for ergonomics and readability before treating the sample as canonical.

## 14. Resource model, links, and post-processing

**Priority:** P2  
**Detailed plan:** `migration\resource-model-links-and-post-processing\plan.md`  
**Depends on:** Phase 1 core + re-query decision gate

### Checklist

- [ ] Write and run a failing proof for the current slice before implementation.
- [ ] Rebuild `ApiResource` metadata and link generation on top of descriptors/plans.
- [ ] Add generated link visibility and discriminator handling.
- [ ] Design resource-aware post-processing against the new pipeline/result model.
- [ ] Finalize the resource-event story without restoring general nested execution.
- [ ] Define concrete resource-event result contracts where needed.

### Acceptance criteria

- [ ] Links and resource metadata are generated from the same canonical data model as the rest of the stack.
- [ ] Resource-event behavior is explicit and testable.
- [ ] Any surviving narrow re-query path is documented, internal, and justified by concrete scenarios.
- [ ] Manifests and OpenAPI stay consistent with the resource model.

### Tests and benchmarks

- [ ] Snapshot tests for links/resource metadata.
- [ ] Integration tests for post-processing and resource-event paths.
- [ ] Performance smoke checks for link-heavy scenarios if string/template handling becomes hot.

### Docs and artifacts

- [ ] Update `docs\architecture.md`.
- [ ] Update `docs\compile-time-validation.md` if link/resource diagnostics evolve.
- [ ] Publish resource/link manifests from the reference app.

### Sub-agent guidance

- Use `explore` to gather the exact legacy resource-event and link behaviors before implementation.
- Use a strong `general-purpose` agent for design and implementation.
- Require `code-review` at the re-query decision gate.

## 15. OpenAPI

**Priority:** P2  
**Detailed plan:** `migration\openapi\plan.md`  
**Depends on:** Phase 1 core + resource/result/serializer decisions

### Checklist

- [ ] Write and run a failing proof for the current slice before implementation.
- [ ] Generate OpenAPI from the same descriptors, result metadata, and serializer metadata used by the HTTP host.
- [ ] Complete the long-term move to HTTP-implied OpenAPI generation by the end of Phase 2.
- [ ] Preserve a customization seam such as `ConfigureOpenApi` only where it still adds value.
- [ ] Treat OpenAPI output as a reviewable artifact, not an opaque side effect.

### Acceptance criteria

- [ ] OpenAPI does not require a separate parallel contract model.
- [ ] The generated output matches the actual HTTP/result/resource behavior.
- [ ] HTTP operations produce OpenAPI automatically unless deliberately disabled for a documented reason.

### Tests and benchmarks

- [ ] Snapshot tests for OpenAPI output.
- [ ] Schema validation for generated documents.
- [ ] Integration tests or comparison checks using the reference app.

### Docs and artifacts

- [ ] Update `docs\architecture.md`.
- [ ] Update the sample/reference documentation with OpenAPI expectations.
- [ ] Publish reference-app OpenAPI output as an artifact.

### Sub-agent guidance

- Use `general-purpose` for implementation.
- Use `task` for snapshot/schema validation runs.
- Use `code-review` before calling the HTTP contract story complete.

## 16. Background tasks

**Priority:** P3  
**Detailed plan:** `migration\background-tasks\plan.md`  
**Depends on:** foundation + operation discovery + pipeline composition

### Checklist

- [ ] Write and run a failing proof for the current slice before implementation.
- [ ] Define non-HTTP host metadata for background-task operations.
- [ ] Reuse the shared pipeline/result/error model where appropriate.
- [ ] Define scheduling/execution semantics and inspection surfaces.

### Acceptance criteria

- [ ] Background work proves the architecture is not HTTP-specific.
- [ ] Host inclusion/execution is still driven by generated metadata.
- [ ] Inspection surfaces explain the registered background-task operations clearly.

### Tests and benchmarks

- [ ] Integration tests for scheduling/execution behavior.
- [ ] Snapshot/manifests for background-task registration.

### Docs and artifacts

- [ ] Update `docs\architecture.md`.
- [ ] Update observability docs if task execution adds special telemetry patterns.

### Sub-agent guidance

- Use `general-purpose` for host design.
- Use `task` for test runs.

## 17. Persistence and transaction coordination

**Priority:** P3  
**Detailed plan:** `migration\persistence-and-transaction-coordination\plan.md`  
**Depends on:** foundation + typed variable registry + pipeline composition

### Checklist

- [ ] Write and run a failing proof for the current slice before implementation.
- [ ] Define generic save-changes/transaction/outbox seams.
- [ ] Prove cross-module variable sharing for persistence scenarios.
- [ ] Keep concrete store packages out of core Blueprint.
- [ ] Provide at least one worked example based on a realistic Signature365-style scenario.

### Acceptance criteria

- [ ] The variable registry supports shared locals/DI values across persistence contributors.
- [ ] The core package remains store-agnostic.
- [ ] A companion package example proves the seam is usable in practice.

### Tests and benchmarks

- [ ] Unit tests for variable consumption/production ordering.
- [ ] Integration tests for a worked persistence pipeline.
- [ ] Performance smoke checks if persistence-related shell work adds overhead.

### Docs and artifacts

- [ ] Update `docs\architecture.md`.
- [ ] Document the extension seam and worked example in the appropriate repo docs.

### Sub-agent guidance

- Use `explore` to compare legacy persistence/outbox middleware with the new variable-registry model.
- Use `general-purpose` for seam implementation.
- Use `code-review` before treating the extension boundary as stable.

## 18. Messaging and integration hosts

**Priority:** P3  
**Detailed plan:** `migration\messaging-and-integration-hosts\plan.md`  
**Depends on:** foundation + operation discovery + pipeline composition + background tasks

### Checklist

- [ ] Write and run a failing proof for the current slice before implementation.
- [ ] Define host metadata and registration for message/integration handlers.
- [ ] Keep message-host concerns in companion packages where appropriate.
- [ ] Prove that alternate hosts can reuse the core descriptor/pipeline/result model.

### Acceptance criteria

- [ ] Alternate hosts work without contaminating the core HTTP architecture.
- [ ] Package-owned integrations are feasible and documented.
- [ ] The seam is strong enough for Rebus-style or similar integrations later.

### Tests and benchmarks

- [ ] Integration tests for host registration/execution.
- [ ] Snapshot/manifests for host metadata.

### Docs and artifacts

- [ ] Update `docs\architecture.md`.
- [ ] Document the package-owned integration pattern in repo docs once the seam exists.

### Sub-agent guidance

- Use `general-purpose` for design and implementation.
- Use `task` for integration validation.
- Use `code-review` before widening the public host-extension surface.

## Standing design decisions the orchestrator should not silently revisit

- Do not port `FramesCollection`, `Frame`, `SyncFrame`, and `AsyncFrame` as the dominant authoring model.
- Use `Blueprint.SourceGenerator.CodeWriter` for final rendering, but keep most behavior declarative in planning models until the final write step.
- Keep module composition explicit through configuration rather than assembly scanning.
- Treat grouped-handler discovery as core Blueprint capability, not a Symprex-only add-on.
- Long-term target: HTTP implies OpenAPI once Phase 2 completes.
- Do not treat general nested execution parity as a goal; solve the specific resource-event scenario deliberately.
- Keep the result model host-agnostic.
- Keep manifests canonical and machine-readable.
- Keep consumer-specific integrations such as Marten/Rebus/resource-expansion ownership in companion packages or consumer space unless there is a strong reason to move them into core.
- A generally reusable non-HTTP host may live in the core Blueprint package, but its namespaces and public type names must keep that host obviously separate from HTTP and package-specific integrations.

## Open questions that require explicit decisions

| Question | Blocks | Must resolve before |
| --- | --- | --- |
| What is the smallest stable public contributor surface that still enables consumer-owned generator packages? | Consumer-owned extension packages, persistence, messaging | Phase 3 public seam freeze |
| What is the canonical `JsonSerializerContext` ownership and validation story? | HTTP host, OpenAPI, result translation | Phase 1 HTTP contract freeze |
| How should `IQuery<T>` / `ICommand<T>` / `IReturn<T>` metadata align with flattened `Results<T1, ...>` outcomes? | Operation discovery, result model, OpenAPI | Result/pipeline contract freeze |
| How much constrained runtime middleware fallback, if any, is worth allowing? | Foundation module system, migration tooling, extension guidance | Phase 0 exit |
| Can resource-event scenarios be solved without any explicit internal re-query path? | Resource model, links, and post-processing | Phase 1 re-query decision gate |

## Human review gates

Treat any dependency reorder, public contributor API change, serializer-context ownership decision, result contract change, auth/security behavior change, or weakening of compile-time guarantees as a major design gate.

Human review is required before closing:

- [ ] Foundation contributor API and planning-model contract
- [ ] Result model and host translation contract
- [ ] Re-query decision gate
- [ ] Authentication/authorisation compatibility model
- [x] Phase 0 exit
- [ ] Phase 1 exit
- [ ] Phase 2 exit
- [ ] First analyzer-led migration release plan

## Final checklist for the future orchestrator

- [ ] Work in the phase order defined here unless a human approves a deviation.
- [ ] Use failing tests/diagnostics/benchmarks first.
- [ ] Use the agent handoff contract when delegating work.
- [ ] Keep each slice small and reviewable.
- [ ] Preserve generator cacheability and invalidation locality.
- [ ] Never approve snapshot baselines blindly; require intentional review.
- [ ] Review generated diffs intentionally.
- [ ] Update docs in the same change.
- [ ] Emit machine-readable artifacts for migrations, manifests, and benchmarks.
- [ ] Use sub-agents deliberately for exploration, implementation, testing, and review.
- [ ] Ask for human review at every major architectural gate.

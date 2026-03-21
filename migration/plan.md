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

## Current status and next recommended step

- Planning and orchestration guidance are complete.
- The initial minimum **Phase 0 / Workstream 1: Foundation module system** slice has landed, including contributor capability splits, deterministic execution-plan diagnostics, `LinkGenerationModule` contributor routing, planning models, and stage-aware pipeline variable validation.
- The next session should treat `migration\foundation-module-system\plan.md` as the immediate detailed execution target, while keeping `migration\plan.md` as the ordering and safety contract.
- The first approved production pipeline-contributor slice is now complete: `HandlerExecutionModule` routes handler invocation plus the produced `handler-result` variable through the activated pipeline seam while preserving the existing generated pipeline output and result wrapping.
- The failing proof for that slice lives in `src\Blueprint.SourceGenerator.Tests\Generators\Pipelines\OperationExecutorPipelineGeneratorIntegrationTests.cs`, and the full pipeline integration class continues to pass after the seam activation.
- The approved follow-on refinement to that slice is now complete locally: contributor methods are now seam-specific (`ContributeToPipeline` / `ContributeToDescriptor`), `PipelineContributorContext` uses `RegisterVariable(...)` plus implicit stage participation instead of explicit `AddContribution(...)`, `PipelineRenderStepKind` has been removed, and `HandlerExecutionModule` owns the handler-invocation rendering callback directly.
- Planning context classes now expose helper methods plus only a small safe read-only metadata surface instead of record-style bags of public mutable inner state, internal variable-registry naming now aligns with `RegisterVariable(...)` / `RequireVariable(...)`, and module-specific helper files now live beside their owning module inside the module folder.
- Concrete module implementations now follow the repo convention of `*Module` naming and live in per-module folders under `src\Blueprint.SourceGenerator\Modules`.
- The second approved production pipeline-contributor slice is now complete: `ValidationModule` detects DataAnnotations/IValidatableObject validation surfaces at compile time, emits `Validator.TryValidateObject(...)` in `PipelineStage.Validation`, and throws `Blueprint.Validation.ValidationException` so the existing validation catch/result path remains unchanged.
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
- Next slice after this redesign: continue with conditional/cross-property rules such as `RequiredIf`-style validation before returning to the deferred exception/result-handling work.
- This remains a narrow seam-activation step, not a freeze of the broader result-model direction. The current result-wrapping behavior is intentionally preserved while host-agnostic result redesign stays deferred.

## Durable decision rule

- `migration\plan.md` is the cross-session source of truth for migration ordering, approved next slices, and human/review decisions.
- Session-local state such as chat history, session `plan.md`, SQL todos, or transient review artifacts is not sufficient to carry a decision into the next orchestrator session.
- If a human or review step approves a next slice, changes architectural direction, or closes a gate, record that decision here and in the relevant `migration\...\plan.md` file before the session ends.

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

1. Read this file first.
2. Read the detailed module plan for the selected workstream before making changes.
3. Do not start a workstream until every listed dependency is complete or an explicit human override is recorded.
4. Implement the smallest reviewable slice that can satisfy a meaningful acceptance criterion.
5. Follow the TDD-first execution loop below for every slice, using the repo-local `test-driven-development`, `systematic-debugging`, and `verification-before-completion` skills when relevant.
6. Update docs, machine-readable artifacts, and relevant Blueprint skill artifacts in the same change.
7. Use the sub-agent guidance in this file to split investigation, implementation, testing, and review work.
8. At every major design gate, request human review before declaring the phase complete.
9. After a slice reaches local done and passes self-review, generate a visual human-review handoff with `visual-explainer` before commit or before moving to the next phase gate.
10. If review closes a decision about sequencing, slice selection, or architecture direction, write that decision explicitly into `migration\plan.md` before ending the session.
11. If a slice is obviously complete, review-closed, and verification-passing, make a small, focused git commit before switching to unrelated work or ending the session. If a review gate remains open, stop short of commit and record why.

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

- [ ] Foundation module system minimum viable slice is implemented.
- [ ] Migration diagnostic ranges, report classes, and analyzer/codemod direction are defined.
- [ ] Tier 2 compile-time scale harness exists and is running deterministically.
- [ ] Generated manifest envelope/schema contract is agreed and reviewable.
- [ ] Benchmark baselines for the known `BlueprintApp` hotspots exist in a reviewable artifact location before Phase 1 starts.
- [ ] The Phase 0 review artifact set exists: manifest schema/example, synthetic fixture example, Tier 2 test evidence, and hotspot benchmark baseline.

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

### Phase 3 - Non-HTTP and consumer-space extensibility

This phase proves that the architecture is general, not HTTP-only.

**Phase 3 exit gate**

- [ ] At least one non-HTTP host works through the shared module/contributor model.
- [ ] At least one consumer-space integration seam is proven with a realistic package-style example.
- [ ] The migration tooling and extension seams are credible enough for Signature365-style migrations beyond the reference app.
- [ ] The Phase 3 review artifact set exists: non-HTTP host manifest, companion-package worked example, and a realistic migration dry-run/report sample.

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
- Validation now flows through `ValidationModule`, which activates only for operations with DataAnnotations validation attributes or `IValidatableObject`, emits `Validator.TryValidateObject(...)` in `PipelineStage.Validation`, and throws `Blueprint.Validation.ValidationException` into the existing catch/result shell.
- The live pipeline now routes both validation and handler execution through contributor-owned render steps while keeping the outer logging plus exception/result shell hard-coded.
- Next recommended slice after the validation review gate: extract exception translation through the activated seam first, while keeping broader result normalization, host wiring, and debug-manifest work deferred.

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
- [ ] Reserve and document migration diagnostic ranges such as `BPMIG001`-`BPMIG399`.
- [ ] Plan and scaffold `Blueprint.Migration.Analyzers`.
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

- [ ] Analyzer diagnostic tests.
- [ ] Code-fix tests with before/after coverage.
- [ ] JSON/SARIF schema validation tests.
- [ ] Dry-run idempotency tests for the codemod CLI.
- [ ] Sample-app migration report review before broad rollout.

### Docs and artifacts

- [ ] Update `docs\refactoring-workflow.md` with the concrete migration workflow and artifact locations.
- [ ] Update `docs\compile-time-validation.md` with migration diagnostic guidance.
- [ ] Add examples of expected migration artifacts once the tooling lands.

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

- [ ] Write and run a failing proof for the current slice before implementation.
- [ ] Build a deterministic synthetic fixture generator for Blueprint apps and operations.
- [ ] Add Tier 2 compile-time cacheability and invalidation-locality tests.
- [ ] Add build/design-time telemetry capture and reporting.
- [ ] Benchmark known runtime hotspots such as `BlueprintApp` registration and operation lookup.
- [ ] Define PR-lane versus scheduled/manual benchmark lanes.
- [ ] Add stress/soak tests for concurrent dispatch, startup, and manifest/OpenAPI generation.

### Acceptance criteria

- [ ] Medium-scale compile-time locality is measured and gated in CI.
- [ ] Build telemetry can explain cold, no-op, and single-edit behavior.
- [ ] Runtime hotspot measurements exist for known registration/lookup bottlenecks.
- [ ] A reviewable benchmark baseline exists for the known `BlueprintApp` hotspots before Phase 1 starts.
- [ ] The fixture generator feeds both compile-time and runtime harnesses.
- [ ] Tier 3 readiness has an explicit measurement plan before Signature365-scale migration starts.

### Tests and benchmarks

- [ ] Cacheability and invalidation-locality tests.
- [ ] Build telemetry capture with `.binlog` and timing summaries.
- [ ] BenchmarkDotNet or equivalent runtime benchmarks for startup, dispatch, and shell overhead.
- [ ] Stress tests for concurrent and high-volume paths.

### Docs and artifacts

- [ ] Update `docs\observability-and-dev-experience.md` with measurement and artifact expectations if needed.
- [ ] Keep benchmark artifact locations and lane expectations documented.
- [ ] Publish benchmark/telemetry outputs in a reviewable format.

### Sub-agent guidance

- Use `general-purpose` for harness design.
- Use `task` for long-running builds/benchmarks.
- Use `code-review` to check whether measurements are representative and whether gates are deterministic.

## 4. Generated manifests and inspection surfaces

**Priority:** P0  
**Detailed plan:** `migration\generated-manifests-and-inspection-surfaces\plan.md`  
**Depends on:** foundation direction

### Checklist

- [ ] Write and run a failing proof for the current slice before implementation.
- [ ] Deliver the Phase 0 contract first: envelope, versioning, ownership, naming, and contributor-collision rules.
- [ ] Define a shared manifest envelope and naming/versioning contract.
- [ ] Define how modules contribute without schema collisions.
- [ ] Make JSON the canonical representation and define projections to markdown/endpoints/tooling.
- [ ] Ensure runtime inspection projects from build-time data rather than recomputing.
- [ ] Align manifest output with migration reports, OpenAPI, and performance artifacts.

### Acceptance criteria

- [ ] The schema is stable enough to snapshot and publish.
- [ ] Phase 0 can close with the shared contract agreed even if later modules still need to populate richer payloads incrementally.
- [ ] At least one real module contributes manifest data through the shared contract.
- [ ] Humans and AI agents can answer "what exists?" and "why?" without reverse-engineering generated code.
- [ ] Runtime inspection does not invent or recompute conflicting data.

### Tests and benchmarks

- [ ] Snapshot tests for manifest shape and content.
- [ ] Schema validation tests.
- [ ] Compatibility tests for multiple module contributors.

### Docs and artifacts

- [ ] Update `docs\observability-and-dev-experience.md`.
- [ ] Update `docs\architecture.md` with manifest generation flow.
- [ ] Publish schema/example artifacts in Phase 0 and richer reference-app artifacts incrementally later.

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
- [ ] Phase 0 exit
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

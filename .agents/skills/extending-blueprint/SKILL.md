---
name: extending-blueprint
description: Use when changing Blueprint generators, runtime contracts, modules, diagnostics, sample-consumer flow, or migration guidance inside the blueprint-source-gen repository
---

# Extending Blueprint

## Overview

Changes in this repo are not just local code edits. Blueprint's generators, runtime, sample consumers, docs, migration plan, and repo-local skills form one contract. A safe change preserves compile-time behavior, deterministic harnesses, and the preferred consumer story together.

## When to Use

- Modifying generator discovery, descriptors, app registration, pipelines, or runtime execution
- Adding module seams, diagnostics, manifests, or inspection surfaces
- Changing the sample consumer story, migration workflow, or Blueprint-specific agent guidance
- Reviewing whether a Blueprint change also requires docs, sample, or skill updates

Do not use this skill for ordinary downstream app usage; use `using-blueprint`.

**REQUIRED BACKGROUND:** Use `using-dotnet`, `dotnet-advisor`, `test-driven-development`, and `verification-before-completion` for this work. Use `systematic-debugging` when tools behave unexpectedly.

## Quick map

| Change area | Start here | Also verify |
| --- | --- | --- |
| Operation contract and descriptors | `docs\architecture.md`, `OperationDescriptorSourceGenerator` | app registration, diagnostics, sample usage |
| App registration | `AppDataModelSourceGenerator` | generated `this.Register(Operation.Configure())`, runtime registry |
| Pipelines and executors | `OperationExecutorPipelineSourceGenerator` | handler discovery, full pipeline snapshots, compile-time source checks, runtime integration harness, concurrent/startup stress harnesses, benchmark harness (`src\Blueprint.SourceGenerator.Benchmarks`) |
| Diagnostics and validation | `docs\compile-time-validation.md` | precise source locations, stable IDs, tests |
| Manifest and inspection contracts | `migration\generated-manifests-and-inspection-surfaces\plan.md`, `GeneratedManifestEnvelope`, `BuildDebugManifest(...)` | `docs\observability-and-dev-experience.md`, snapshot proof, contributor ownership collisions |
| Consumer story | sample projects + `docs\refactoring-workflow.md` | `using-blueprint`, migration prompt, docs |

## Working rules

1. Start in `/docs`, then find the deterministic harness before touching production code.
2. Preserve incremental cacheability and review the generated diff, not just handwritten files.
3. Follow through across generator, runtime, sample, docs, and skills when the contract changes.
4. If Blueprint becomes easier or harder to consume, update `using-blueprint` in the same change.
5. If the repo-internal extension workflow changes, update this skill in the same change.
6. For migration orchestrator work, keep `migration\progress.json`, `migration\plan.md`, and the relevant workstream plan aligned. Treat the JSON file as the machine-readable resume index, not as a replacement for the plan narrative.
7. For migration orchestrator work, finding the next ready slice is not enough. The same session must continue into failing proof and the first implementation step unless an explicit blocker or review gate stops the change.
8. Concrete module implementations must end with `Module`, live in dedicated subfolders under `src\Blueprint.SourceGenerator\Modules`, and keep module-specific helper files beside the owning module in that folder.
9. Contributor seam methods should use explicit `ContributeTo<Thing>` names, and planning context classes should prefer helper methods with only a small read-only metadata surface rather than record-style public inner state or mutable plan/execution access.
10. For pipeline planning, prefer helper APIs that imply stage participation when contributors register render steps or variables; do not reintroduce manual `AddContribution(...)` bookkeeping.
11. For pipeline contributor feature work, require the full proof matrix: a full pipeline snapshot, compile-time invalid-configuration or source-check tests when the generator can validate the contract, and runtime integration tests that execute the generated pipeline for valid and invalid paths. A substring assertion alone is not enough.
12. For validation or executor hot-path work, run `src\Blueprint.SourceGenerator.Benchmarks` and review the BenchmarkDotNet artifact before handoff or commit. Keep deterministic checks in the `pull-request lane`, and move noisier wall-clock or higher-volume comparisons into a `scheduled/manual` lane instead of treating shared-runner timing as an ordinary PR gate. When the slice is about stability under concurrency or repeated startup/host mapping instead of pure cost, pair that benchmark evidence with a non-microbenchmark runtime stress proof in tests so the generated executor shell or `MapBlueprintApi(...)` host surface is exercised on the same seam under repeated load.
13. For validation fast-path work, keep rule detection and code emission in dedicated strategy helpers beside `ValidationModule`; use `ValidationRuntime` for unsupported per-property fallback and add `BPV` diagnostics when referenced members are provably invalid.
14. New manifest producers should extend `Blueprint.SourceGenerator.Artifacts.GeneratedManifestEnvelope` and its default `.blueprint/manifests/<artifact>.manifest.json` layout instead of inventing unrelated top-level JSON contracts or insertion-order-dependent output. Keep the checked-in schema artifact at `src\Blueprint.SourceGenerator\Artifacts\GeneratedManifestEnvelope.schema.json` aligned with that envelope contract, and treat the current operations-discovery payload shape as an approved starting point, not as a forever-frozen contract if later debug-manifest improvements need a better contributor-owned projection. Cross-workstream performance artifacts now follow that same rule too: the Tier 2 synthetic invalidation summary is a `scale-invalidation` envelope with a contributor-owned `synthetic-scale-harness` section, and the first build telemetry contract is a sibling `build-telemetry` envelope with `synthetic-build-harness`; neither should regress into ad-hoc performance-only JSON shapes. The repo-native build telemetry runner is `migration\performance-and-scale\Capture-SyntheticBuildTelemetry.ps1`, and it now emits cold-build, no-op-build, design-time-build, and single-edit-build captures through that same envelope. Any synthetic project it materializes must keep emitted inspection files out of `Compile` (for example with `Compile Remove="artifacts\generated\**\*.cs"`) so repeated no-op or design-time captures do not re-ingest stale generated output.
15. Derived manifest projections such as `GeneratedManifestEnvelope.ToMarkdown()` must read only envelope data, and they should keep nested payload ordering deterministic so future richer manifest content stays reviewable. Hosted markdown fixtures should stay semantically aligned with `GeneratedManifestEnvelope.FromJson(...).ToMarkdown()` rather than drifting from the canonical JSON contract.
16. Runtime inspection slices should load previously built manifest data through stable seams such as `ManifestResourceLoader` plus `GeneratedManifestEnvelope.FromJson(...)`; do not rebuild discovery by calling contributor seams or re-deriving operation metadata in runtime code. If the runtime project itself hosts an inspection surface, prefer serving canonical embedded JSON via an explicit resource-name seam (for example `BlueprintHttpOptions.OperationsManifestResourceName`) instead of adding a `Blueprint.Runtime` reference back to `Blueprint.SourceGenerator`. Richer hosted views such as diagnostics pages should project client-side from that JSON rather than introducing a second runtime-side manifest model, and hosted markdown or schema surfaces should be served as prebuilt embedded bytes (for example via `BlueprintHttpOptions.OperationsManifestMarkdownResourceName` and `BlueprintHttpOptions.OperationsManifestSchemaResourceName`) rather than calling `GeneratedManifestEnvelope.ToMarkdown()` or interpreting schema semantics inside the runtime assembly.
17. Manifest contributor slices should activate the typed `ContributeToManifest(DebugManifestContributorContext)` seam and the public `BlueprintSourceGenerationConfig.BuildDebugManifest(...)` helper instead of bypassing the planning model with ad-hoc envelope writes. Treat per-operation debug-manifest entry keys as contributor-owned: if two contributors need the same concept, redesign the payload or section shape instead of relying on overwrite behavior, because `BuildDebugManifest(...)` should now fail with `BPMOD006` on cross-contributor key collisions.
18. When comments are needed, spend them on invariants, fallback boundaries, and trade-offs; never on repeating what the code already says.
19. Prefer `static` lambdas in LINQ or similar delegate-heavy code whenever no local capture is needed.
20. Keep module-specific tests in module-specific files instead of extending shared catch-all test files forever.

## Common mistakes

- Fixing one seam without checking runtime, sample, and docs
- Adding runtime repair where compile-time validation should exist
- Blindly updating snapshots without reading the generated diff
- Changing consumer-facing behavior without updating `using-blueprint`
- Treating a generated-source line check as the only proof for pipeline behavior

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
| Pipelines and executors | `OperationExecutorPipelineSourceGenerator` | handler discovery, full pipeline snapshots, compile-time source checks, runtime integration harness, benchmark harness (`src\Blueprint.SourceGenerator.Benchmarks`) |
| Diagnostics and validation | `docs\compile-time-validation.md` | precise source locations, stable IDs, tests |
| Consumer story | sample projects + `docs\refactoring-workflow.md` | `using-blueprint`, migration prompt, docs |

## Working rules

1. Start in `/docs`, then find the deterministic harness before touching production code.
2. Preserve incremental cacheability and review the generated diff, not just handwritten files.
3. Follow through across generator, runtime, sample, docs, and skills when the contract changes.
4. If Blueprint becomes easier or harder to consume, update `using-blueprint` in the same change.
5. If the repo-internal extension workflow changes, update this skill in the same change.
6. Concrete module implementations must end with `Module`, live in dedicated subfolders under `src\Blueprint.SourceGenerator\Modules`, and keep module-specific helper files beside the owning module in that folder.
7. Contributor seam methods should use explicit `ContributeTo<Thing>` names, and planning context classes should prefer helper methods with only a small read-only metadata surface rather than record-style public inner state or mutable plan/execution access.
8. For pipeline planning, prefer helper APIs that imply stage participation when contributors register render steps or variables; do not reintroduce manual `AddContribution(...)` bookkeeping.
9. For pipeline contributor feature work, require the full proof matrix: a full pipeline snapshot, compile-time invalid-configuration or source-check tests when the generator can validate the contract, and runtime integration tests that execute the generated pipeline for valid and invalid paths. A substring assertion alone is not enough.
10. For validation or executor hot-path work, run `src\Blueprint.SourceGenerator.Benchmarks` and review the BenchmarkDotNet artifact before handoff or commit.
11. For validation fast-path work, keep rule detection and code emission in dedicated strategy helpers beside `ValidationModule`; use `ValidationRuntime` for unsupported per-property fallback and add `BPV` diagnostics when referenced members are provably invalid.
12. When comments are needed, spend them on invariants, fallback boundaries, and trade-offs; never on repeating what the code already says.
13. Prefer `static` lambdas in LINQ or similar delegate-heavy code whenever no local capture is needed.
14. Keep module-specific tests in module-specific files instead of extending shared catch-all test files forever.

## Common mistakes

- Fixing one seam without checking runtime, sample, and docs
- Adding runtime repair where compile-time validation should exist
- Blindly updating snapshots without reading the generated diff
- Changing consumer-facing behavior without updating `using-blueprint`
- Treating a generated-source line check as the only proof for pipeline behavior

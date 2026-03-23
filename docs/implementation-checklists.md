# Implementation checklists

Use these checklists when making common kinds of changes in this repository.

They are intentionally short and repeatable so future humans and AI agents can use them as a working filter, not just background reading.

## Cross-cutting checklist

- [ ] Does this push work to compile time instead of runtime where possible?
- [ ] Are new string-based contracts validated against symbols or metadata instead of trusted blindly?
- [ ] Does the runtime stay AOT-friendly, reflection-light, and easy to inspect?
- [ ] Are observability and diagnostics considered part of the feature, not follow-up work?
- [ ] If the change affects architecture, diagnostics, observability, or workflow, did `/docs` get updated too?
- [ ] Do public, protected, and internal non-test C# surfaces have rationale-focused XML docs, and are reusable test helpers documented even though actual `*Test.cs` / `*Tests.cs` files stay exempt?

## New operation or app-registration work

- [ ] Keep operation metadata flowing through `Configure()` and generated descriptors.
- [ ] Ensure app registration still happens through generated `this.Register(Operation.Configure())` patterns.
- [ ] Prefer compile-time discovery over runtime registration rules.
- [ ] If the change touches routes, links, or metadata, check whether `LinkGenerationModule` or another module should enforce it.
- [ ] Update the relevant snapshot baselines only after reviewing the generated diff.

## New generator or module work

- [ ] Check whether the change belongs in descriptor generation, app registration, pipeline generation, or a module.
- [ ] If a module is the right seam, implement the behavior through the contributor seam rooted at `IBlueprintSourceGeneratorModule` and resolved by `BlueprintSourceGenerationConfig.BuildExecutionPlan()`.
- [ ] Prefer real contributor implementations over temporary legacy adapters; add a migration adapter only when another module truly needs staged compatibility scaffolding.
- [ ] Add or refine diagnostics with stable IDs and actionable messages.
- [ ] Preserve incremental-generator cacheability and rerun the cache-sensitive test harness.
- [ ] Prefer CodeWriter helpers over ad-hoc string concatenation.
- [ ] For new planning helpers, registries, or contributor-ordering logic, add tests for happy paths, failure/diagnostic paths, and determinism edge cases.
- [ ] If the change activates pipeline planning, cover both the missing-required-variable diagnostic path and an ordered shared-variable happy path.
- [ ] If the change adds or alters pipeline contributor behavior, include all three proof layers: a full pipeline snapshot, compile-time invalid-configuration/diagnostic coverage when the generator can know the contract is wrong, and runtime integration tests that execute the generated pipeline for valid and invalid paths.
- [ ] Do not treat a “generated source contains line X” assertion as sufficient proof for pipeline behavior; use it only as a supporting check alongside the stronger harnesses.
- [ ] If the change targets a measured pipeline or validation hot path, run `src\Blueprint.SourceGenerator.Benchmarks` and capture the relevant BenchmarkDotNet artifact before closing the slice.
- [ ] If comments are needed, make them explain intent, constraints, invariants, or trade-offs that are not obvious from the code itself. Do not add narration comments.
- [ ] Prefer `static` lambdas in LINQ or similar delegate-heavy code when no capture is required.
- [ ] Keep module-specific tests in module-specific files instead of extending shared catch-all test classes indefinitely.
- [ ] Do not use reflection in tests to assert contract existence or invoke private/internal helpers; if behavior is hard to prove, add or use a supported seam instead.

## New validation or diagnostic work

- [ ] Validate at the earliest stage with enough symbol information.
- [ ] Point to the most useful source location, including subranges when practical.
- [ ] Include concrete type, member, route, or serializer names in the message.
- [ ] Add tests that lock in both the success path and the failure path.
- [ ] If a validation attribute or other declarative contract names another symbol with a string (for example `CompareAttribute.OtherProperty`), add a compile-time diagnostic and tests for missing or invalid references when the generator can validate it.
- [ ] Ask whether the diagnostic should also be documented in `docs/compile-time-validation.md`.

## Serialization and API contract work

- [ ] Prefer `System.Text.Json` source-generation patterns over runtime serializer discovery.
- [ ] If a feature depends on serializer metadata, consider whether a `JsonSerializerContext` requirement should be explicit and diagnosable.
- [ ] Avoid silent runtime fallbacks when the build could have detected a missing contract.
- [ ] Make the contract inspectable enough that humans and AI agents can see what serialization surface is expected.

## Runtime and observability work

- [ ] Add or preserve structured tracing, logging, or metadata for the new path.
- [ ] Favor OpenTelemetry and OTLP-compatible thinking over bespoke observability silos.
- [ ] Keep large API surfaces inspectable through registries, manifests, endpoints, or tooling.
- [ ] If a new runtime behavior is difficult to explain, add diagnostics, metadata, or developer tooling that makes it easier to inspect.
- [ ] Check whether `docs/observability-and-dev-experience.md` needs to change.

## AI- and developer-tooling work

- [ ] Prefer stable, machine-readable outputs over log scraping.
- [ ] Make large generated surfaces easier to summarize, inspect, and reason about.
- [ ] Design tooling so it helps both humans and AI agents answer "what exists?" and "why?" quickly.
- [ ] If a tool closes a recurring confusion gap, document the new workflow in `/docs`.
- [ ] If the change alters how Blueprint is consumed or extended, update `using-blueprint` and/or `extending-blueprint` in the same change.

## Breaking change or migration work

- [ ] Choose the right refactoring layer from `docs\refactoring-workflow.md` before making bulk changes.
- [ ] Write any approved sequencing, slice-selection, or architecture decisions back into `migration\plan.md` and the relevant `migration\...\plan.md` file before ending the session.
- [ ] For a consumer-app migration, capture the legacy baseline before applying fixes.
- [ ] Prefer Roslyn analyzers/code fixes or a codemod CLI over regex-first structural edits.
- [ ] For large codemods, produce and review a dry-run report before applying changes.
- [ ] Emit machine-readable migration artifacts (`.sarif`, `.json`) and a short human-readable summary.
- [ ] For an app migration, produce and review the before/after manifests, OpenAPI, and equivalence report.
- [ ] Run build, formatting, relevant tests, and snapshot review after the refactor.
- [ ] Update the migration plan and `docs\refactoring-workflow.md` if the workflow, commands, or artifact contract changes.

## Final validation checklist

- [ ] Run the relevant tests, snapshots, and formatting flow.
- [ ] Review the generated diff, not just the source diff.
- [ ] Make sure the docs and harness changes match the implementation.
- [ ] If a benchmark harness exists for the touched hot path, rerun it and review the output before handoff or commit.
- [ ] Re-run the final verification commands immediately before claiming success or commit readiness.
- [ ] If the slice is obviously complete and no review gate remains open, make a small, focused git commit before switching tasks or ending the session.
- [ ] Leave the repository easier to change correctly than it was before.

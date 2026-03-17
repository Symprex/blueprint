# Implementation checklists

Use these checklists when making common kinds of changes in this repository.

They are intentionally short and repeatable so future humans and AI agents can use them as a working filter, not just background reading.

## Cross-cutting checklist

- [ ] Does this push work to compile time instead of runtime where possible?
- [ ] Are new string-based contracts validated against symbols or metadata instead of trusted blindly?
- [ ] Does the runtime stay AOT-friendly, reflection-light, and easy to inspect?
- [ ] Are observability and diagnostics considered part of the feature, not follow-up work?
- [ ] If the change affects architecture, diagnostics, observability, or workflow, did `/docs` get updated too?

## New operation or app-registration work

- [ ] Keep operation metadata flowing through `Configure()` and generated descriptors.
- [ ] Ensure app registration still happens through generated `this.Register(Operation.Configure())` patterns.
- [ ] Prefer compile-time discovery over runtime registration rules.
- [ ] If the change touches routes, links, or metadata, check whether `LinkGenerationModule` or another module should enforce it.
- [ ] Update the relevant snapshot baselines only after reviewing the generated diff.

## New generator or module work

- [ ] Check whether the change belongs in descriptor generation, app registration, pipeline generation, or a module.
- [ ] If a module is the right seam, implement the behavior through `IBlueprintSourceGeneratorModule`.
- [ ] Add or refine diagnostics with stable IDs and actionable messages.
- [ ] Preserve incremental-generator cacheability and rerun the cache-sensitive test harness.
- [ ] Prefer CodeWriter helpers over ad-hoc string concatenation.

## New validation or diagnostic work

- [ ] Validate at the earliest stage with enough symbol information.
- [ ] Point to the most useful source location, including subranges when practical.
- [ ] Include concrete type, member, route, or serializer names in the message.
- [ ] Add tests that lock in both the success path and the failure path.
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

## Breaking change or migration work

- [ ] Choose the right refactoring layer from `docs\refactoring-workflow.md` before making bulk changes.
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
- [ ] Leave the repository easier to change correctly than it was before.

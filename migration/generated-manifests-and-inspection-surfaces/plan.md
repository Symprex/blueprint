# Generated manifests and inspection surfaces

- Status: In progress (contract approved; first contributor slice ready)
- Priority: P0
- Classification: Cross-cutting architecture support
- Depends on: no hard dependency for the first contract-definition pass; then evolves alongside `../foundation-module-system/plan.md`, `../operation-discovery-and-descriptors/plan.md`, `../auditing-and-observability/plan.md`, and `../performance-and-scale/plan.md`

## Why this module exists

Many migration plans already rely on generated manifests or inspection surfaces:

- operation discovery wants readable summaries of discovered operations
- pipeline composition wants stage/contributor manifests
- auth wants `operations.auth` and later projection artifacts
- result modeling wants flattened outcome manifests
- resources and links want generated route/link visibility
- observability wants AI-friendly inspection surfaces
- performance wants machine-readable scale artifacts

Without one shared contract, each workstream will invent its own file names, schemas, and ownership rules.

## Evidence

- `../foundation-module-system/plan.md`
- `../operation-discovery-and-descriptors/plan.md`
- `../pipeline-composition-and-execution/plan.md`
- `../result-model-and-host-translation/plan.md`
- `../authentication-and-authorisation/plan.md`
- `../resource-model-links-and-post-processing/plan.md`
- `../auditing-and-observability/plan.md`
- `../performance-and-scale/plan.md`
- `..\docs\observability-and-dev-experience.md`

## Migration goal

Define one coherent contract for generated manifests and inspection surfaces so that:

- build outputs are structured and predictable
- runtime inspection surfaces project from the same underlying data
- snapshot tests, CI artifacts, and AI-agent inspection use the same source of truth
- modules can add information without inventing incompatible output formats

## Artifact families

The first design pass should distinguish a small number of artifact families.

### 1. Build-time manifests

Structured outputs emitted during build/generation, for example:

- operations/discovery summaries
- pipeline-stage summaries
- auth manifests
- flattened result manifests
- link/resource manifests

These are the canonical machine-readable form.

### 2. Runtime inspection surfaces

Developer-facing or AI-facing views that project from the generated/build-time data, for example:

- embedded resources
- dev-only endpoints
- CLI/tooling summaries

The runtime should not recompute meaning that the build already knew.

### 3. Test and review artifacts

Reviewable projections derived from the same structured data, for example:

- snapshot baselines
- markdown summaries
- CI artifacts

These exist for humans, but they should derive from the machine-readable source rather than becoming the only artifact.

## Canonical contract to define

This module should own the shared answers to:

- where artifacts live by default
- which artifacts are file-system outputs versus embedded/generated runtime data
- naming conventions
- minimum envelope fields such as app/assembly identity and generator version
- how per-operation entries are keyed
- how contributors extend the schema without collisions
- which artifacts are stable enough for snapshot testing and CI review

## Ownership model

Planning direction:

- the foundation layer defines the contributor seam for manifest data
- this module defines the schema/layout rules
- individual feature modules own their specific payload sections
- observability and runtime tooling consume the common contract rather than bypassing it

That keeps artifact ownership explicit while avoiding one giant monolithic manifest model.

## File and schema strategy

Preferred direction:

- JSON or an equivalently machine-readable structured format is the canonical artifact form
- markdown summaries are derived views, not the source of truth
- artifact names should reflect the subject clearly, for example `operations.manifest.json`, `operations.auth.json`, or other stable names once finalized
- per-module payloads should sit inside a shared envelope rather than inventing unrelated top-level schemas

The exact file names can still be finalized later, but the repository should close the envelope/extension model before multiple modules emit their own artifacts.

## Runtime surface strategy

Generated inspection surfaces should follow these rules:

- prefer projecting from build-time artifact data rather than re-discovering the app shape at runtime
- keep developer/debug surfaces opt-in or dev-focused where exposure could be sensitive
- expose enough information that large API surfaces can be inspected without reading generated C#

## Testing and CI expectations

Manifest design is part of the deterministic harness, not an afterthought.

Expect:

- snapshot coverage for stable manifest shapes
- CI artifact publication where the workflow benefits from it
- machine-readable artifacts consumable by AI agents and tooling
- clear rules for when a manifest shape change is an intentional review item

## Relationship to performance and migration tooling

This plan does not replace:

- migration SARIF/codemod reports
- performance benchmark artifacts

But it should align with them where shared conventions help, especially around machine-readable outputs, naming, and reviewability.

## Explicit decisions

- Generated manifests are a first-class architectural surface, not optional polish.
- Machine-readable structured outputs are canonical; markdown or endpoint views are projections.
- Runtime inspection should project from generated/build-time data wherever possible.
- Modules may extend manifest content, but they should not invent incompatible envelopes or naming schemes.

## Sequencing

Define the common contract early, before multiple modules emit their own artifact formats.

The first milestone is the shared envelope, naming/layout rules, and contributor ownership model. Specific payload sections can then land incrementally with each feature module.

## Current first-milestone status

- `src\Blueprint.SourceGenerator\Artifacts\GeneratedManifestEnvelope.cs` now defines the first shared build-time manifest contract.
- The current contract fixes `build-time-manifest` as the initial artifact family and `.blueprint/manifests/<artifact>.manifest.json` as the default relative layout for canonical build outputs.
- The envelope carries stable app identity (`typeName`, `assemblyName`) plus generator identity (`productName`, `version`) so runtime tooling, CI artifacts, and review projections can project from one source of truth.
- Top-level contributor payloads now live under shared `sections` entries with explicit `contributorId` ownership, conflicting owners for the same section name fail fast instead of silently merging incompatible schemas, and serialization now uses stable ordinal key ordering instead of contributor insertion order.
- Per-operation payloads now live under shared `operations` entries keyed by a stable operation identifier, duplicate keys are rejected to keep downstream projections deterministic, and serialization also uses stable ordinal key ordering.
- `src\Blueprint.SourceGenerator.Tests\Artifacts\GeneratedManifestEnvelopeTests.cs` plus `GeneratedManifestEnvelopeTests.CreateBuildManifest_Should_Serialize_Stable_Envelope.verified.txt` provide the first snapshot/contract proof for this workstream.
- Human review approved the initial envelope fields, default layout, ownership rules, and deterministic key-ordering fix for downstream manifest-emitting slices.
- Next approved slice: land operations/discovery summaries as the first real module contributor through the shared envelope, starting with snapshot, ownership-collision, and integration proofs.

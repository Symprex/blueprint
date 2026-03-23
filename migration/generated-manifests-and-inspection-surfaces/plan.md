# Generated manifests and inspection surfaces

- Status: In progress (runtime projections active; hosted schema endpoint and first performance-artifact alignment slice complete locally)
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
- The first real contributor slice is now complete locally and ready for human review: `IDebugManifestContributor` exposes `ContributeToManifest(DebugManifestContributorContext)`, `BlueprintSourceGenerationConfig.BuildDebugManifest(...)` activates the seam, `OperationDiscoveryManifestModule` writes `kind`/`namespace`/`typeName` entries for discovered operations, and `OperationsDiscoveryManifestEmitter` projects those per-operation manifests into the approved shared envelope under the `operations-discovery` section plus canonical `operations` entries.
- `src\Blueprint.SourceGenerator.Tests\Modules\OperationDiscoveryManifestModuleTests.cs` now covers the public seam activation, duplicate-operation-key rejection through the emitter, the reviewed snapshot in `src\Blueprint.SourceGenerator.Tests\Modules\OperationDiscoveryManifest\Snapshots\OperationDiscoveryManifestModuleTests.When_emitted_then_operations_manifest_serializes_stable_summary.verified.txt`, namespace-safe kind classification, and the deterministic `BPMOD005` missing-operation diagnostic for unresolved operation types.
- A fresh `dotnet format`, `dotnet build`, and `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --no-restore` pass completed cleanly after the review fixes, and second-pass code review found no remaining blocking issues.
- Human review approved that first real contributor shape as the current foundation for later multi-module manifest enrichment and runtime projection work.
- The current operations-discovery entry shape (`kind`, `namespace`, `typeName`) is approved for now, but it is not a forever-frozen contract. Later debug-manifest improvements may diverge from that payload when a better contributor-owned shape is warranted, provided the change still goes through the shared envelope plus normal review/versioning gates.
- The first derived projection slice is now human-approved: `GeneratedManifestEnvelope.ToMarkdown()` produces a deterministic markdown summary from envelope data alone, `ManifestMarkdownProjectionTests` cover the stable operations summary snapshot plus the envelope-only guard, and nested `JObject`/`JArray` payloads are normalized recursively so richer future payloads keep stable markdown ordering.
- The runtime manifest loading and embedded resource projection slice is now human-approved: `Blueprint.Runtime.Artifacts.ManifestResourceLoader` loads the approved operations manifest JSON from an embedded assembly resource, `GeneratedManifestEnvelope.FromJson(...)` roundtrips the shared envelope contract for runtime consumption, and `ManifestRuntimeProjectionTests` prove embedded-resource loading, stable roundtripping, and runtime projection without recomputing operation discovery.
- A full verification pass completed cleanly for that slice: the targeted `dotnet format` include set, `dotnet build .\Blueprint.sln --no-restore`, and `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --no-restore` all pass, the runtime snapshot is approved with no remaining `.received` baseline, and independent code review found no significant issues.
- Human review approved the embedded-resource seam as the current dependency boundary before wider runtime inspection surfaces land.
- The dev-only manifest JSON inspection endpoint slice is now human-approved: `MapBlueprintApi(...)` exposes `/.blueprint/manifests/operations.json` beneath the configured API base path only in development, `BlueprintHttpOptions.OperationsManifestResourceName` supplies the explicit embedded-resource seam, and `ManifestJsonInspectionEndpoint` serves the canonical manifest bytes without adding a runtime reference back to `Blueprint.SourceGenerator` or re-running discovery.
- A focused red/green proof now covers both the hosted JSON happy path and the fail-fast misconfiguration case: `ManifestInspectionEndpointTests` proves the route is present in development, absent in production, returns the embedded manifest JSON unchanged, and now throws during endpoint mapping when the configured resource name is invalid.
- A full verification pass completed cleanly for this slice: the targeted `dotnet format` include set, `dotnet build .\Blueprint.sln --no-restore`, and `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --no-restore` all pass, and second-pass code review found no remaining significant issues.
- Markdown and other richer human-readable hosted projections remain deferred until the runtime/tooling boundary is widened deliberately, because the current runtime project still intentionally does not reference the source-generator manifest model.
- Human review approved this dev-only hosted JSON seam as the next inspection-surface boundary before any broader production exposure, CLI-specific affordances, or alternate inspection contracts are introduced.
- The development-only manifest diagnostics page slice is now human-approved: `MapBlueprintApi(...)` exposes `/.blueprint/manifests/operations.html` beside the existing JSON endpoint in development, and `ManifestDiagnosticsPageEndpoint` keeps the runtime boundary intact by fetching sibling `operations.json` client-side instead of introducing a second runtime-side manifest model.
- `ManifestInspectionEndpointTests` now cover the HTML route's development-only exposure, production dark path, stable HTML shell snapshot, and the regression guard that raw fetch error details stay in the browser console instead of being echoed into the page body.
- A full verification pass completed cleanly for this slice after the review-driven hardening fix: targeted `dotnet format` include runs clean, `dotnet build .\Blueprint.sln --no-restore` succeeds, `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --no-restore` passes with 172 tests green, and independent code review found no remaining significant issues.
- Human review approved this diagnostics-page boundary as the current hosted follow-on: richer development-time inspection may grow through thin hosted shells over canonical JSON, but `Blueprint.Runtime` still should not gain its own manifest model or a reference back to `Blueprint.SourceGenerator`.
- The development-only hosted markdown manifest endpoint is now human-approved: it serves prebuilt markdown bytes through `BlueprintHttpOptions.OperationsManifestMarkdownResourceName`, so runtime hosting widens inspection without teaching `Blueprint.Runtime` how to project markdown from JSON itself.
- `ManifestInspectionEndpointTests` now cover the markdown route's development-only exposure, production dark path, and byte-for-byte response against an embedded markdown fixture that mirrors the approved build-time markdown projection.
- A full verification pass completed cleanly for this slice: targeted `dotnet format` include runs clean, `dotnet build .\Blueprint.sln --no-restore` succeeds, `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --no-restore` passes with 173 tests green, and independent code review found no remaining significant issues.
- Human review approved this markdown-hosting boundary as the current widening step: runtime inspection may host prebuilt markdown artifacts, but it still must not compute markdown or manifest meaning inside `Blueprint.Runtime`.
- The deterministic debug-manifest collision slice is now complete locally and ready for human review: `GeneratedDebugManifest` tracks entry ownership and collisions, `DebugManifestContributorContext` carries contributor identity into manifest writes, and `BlueprintSourceGenerationConfig.BuildDebugManifest(...)` now surfaces `BPMOD006` instead of silently letting the later contributor overwrite an earlier entry.
- `src\Blueprint.SourceGenerator.Tests\Modules\DebugManifestContributorCompatibilityTests.cs` now proves both sides of that contract through the public seam: conflicting contributors fail deterministically, while non-conflicting contributors still compose into one manifest.
- A full verification pass completed cleanly for this slice: the focused red/green proof passes, `dotnet format .\Blueprint.sln --no-restore --include ...` is clean for the touched C# files, `dotnet build .\Blueprint.sln --no-restore` succeeds, and `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --no-restore` passes with 175 tests green.
- Human review approved this collision-diagnostic boundary: debug-manifest entry keys now belong to the contributor that claims them first, and later contributors must use a different key or payload shape instead of silently overwriting the entry.
- The first schema-validation sub-slice is now complete locally: `GeneratedManifestEnvelope.FromJson(...)` no longer silently ignores malformed top-level `sections` or `operations` properties, and `GeneratedManifestEnvelopeTests` now prove those properties must be JSON objects when present.
- A full verification pass completed cleanly for this sub-slice: the focused malformed-shape tests pass, `dotnet format .\Blueprint.sln --no-restore --include src\Blueprint.SourceGenerator\Artifacts\GeneratedManifestEnvelope.cs src\Blueprint.SourceGenerator.Tests\Artifacts\GeneratedManifestEnvelopeTests.cs` is clean aside from the repo's known IDE1006 noise, `dotnet build .\Blueprint.sln --no-restore` succeeds, and `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --no-restore` passes with 177 tests green.
- The next schema-validation sub-slice is now also complete locally: `GeneratedManifestEnvelope.FromJson(...)` no longer accepts a quoted numeric token for `schemaVersion`, and `GeneratedManifestEnvelopeTests` now prove the manifest schema version must be a real JSON integer token instead of a permissively converted string.
- A full verification pass completed cleanly for this sub-slice too: the focused `schemaVersion` invalid-token tests pass, `dotnet format .\Blueprint.sln --no-restore --include src\Blueprint.SourceGenerator\Artifacts\GeneratedManifestEnvelope.cs src\Blueprint.SourceGenerator.Tests\Artifacts\GeneratedManifestEnvelopeTests.cs` is clean aside from the repo's known IDE1006 noise, `dotnet build .\Blueprint.sln --no-restore` succeeds, and `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --no-restore` passes with 179 tests green.
- The next schema-validation sub-slice is now also complete locally: `GeneratedManifestEnvelope.FromJson(...)` no longer accepts numeric tokens for required string fields through permissive `Value<string>` conversions, and `GeneratedManifestEnvelopeTests` now pin that contract with a failing-proof-first `artifactName` invalid-token test.
- A full verification pass completed cleanly for this sub-slice too: the focused required-string invalid-token test passes, `dotnet format .\Blueprint.sln --no-restore --include src\Blueprint.SourceGenerator\Artifacts\GeneratedManifestEnvelope.cs src\Blueprint.SourceGenerator.Tests\Artifacts\GeneratedManifestEnvelopeTests.cs` is clean aside from the repo's known IDE1006 noise, `dotnet build .\Blueprint.sln --no-restore` succeeds, and `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --no-restore` passes with 180 tests green.
- The next dependency-safe schema-validation follow-on is now complete locally: `GeneratedManifestEnvelope.FromJson(...)` now reports full manifest paths for nested required-string failures in `app`, `generator`, and contributor-owned sections, and `GeneratedManifestEnvelopeTests` now pin invalid-token coverage for `app.typeName`, `app.assemblyName`, `generator.productName`, `generator.version`, and `sections.operations-discovery.contributorId`.
- A full verification pass completed cleanly for this sub-slice too: the focused nested-path red/green proof passes, `dotnet format .\Blueprint.sln --no-restore --include src\Blueprint.SourceGenerator\Artifacts\GeneratedManifestEnvelope.cs src\Blueprint.SourceGenerator.Tests\Artifacts\GeneratedManifestEnvelopeTests.cs` is clean aside from the repo's known IDE1006 noise, `dotnet build .\Blueprint.sln --no-restore` succeeds, and `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --no-restore` passes with 185 tests green.
- No contract drift or new schema-design gate surfaced in that proof, so the next recommended follow-on in this workstream is to add machine-readable schema validation for the approved manifest envelope and hosted fixture artifacts before wider runtime or tooling projections widen again.
- The machine-readable schema-validation follow-on is now complete locally: `src\Blueprint.SourceGenerator\Artifacts\GeneratedManifestEnvelope.schema.json` now defines the approved envelope contract in a checked-in machine-readable artifact, `GeneratedManifestEnvelopeTests` validate the embedded operations manifest against that schema plus an invalid `app.typeName` mutation, and `ManifestInspectionEndpointTests` now pin the hosted markdown fixture semantically to `GeneratedManifestEnvelope.FromJson(...).ToMarkdown()`.
- A full verification pass completed cleanly for this schema slice too: targeted schema tests pass, `dotnet format .\Blueprint.sln --no-restore --include src\Blueprint.SourceGenerator.Tests\Artifacts\GeneratedManifestEnvelopeTests.cs src\Blueprint.SourceGenerator.Tests\Artifacts\ManifestInspectionEndpointTests.cs` is clean aside from the repo's known IDE1006 noise, `dotnet build .\Blueprint.sln --no-restore` succeeds, and `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --no-restore` passes with 188 tests green.
- Phase 0 now has both a checked-in schema artifact and checked-in example manifest/markdown fixtures for the approved operations manifest contract.
- The user has now approved autonomous Phase 0 direction, so this workstream continued down the runtime-inspection branch first. The hosted schema endpoint slice is now complete locally: `MapBlueprintApi(...)` exposes `/.blueprint/manifests/operations.schema.json` in development when `BlueprintHttpOptions.OperationsManifestSchemaResourceName` is configured, and `ManifestSchemaInspectionEndpoint` serves the embedded schema bytes unchanged without teaching `Blueprint.Runtime` how to interpret or project schema semantics itself.
- `ManifestInspectionEndpointTests` now cover the schema route's development-only exposure, production dark path, exact embedded-schema response, and fail-fast invalid schema-resource configuration through the hosted boundary.
- A full verification pass completed cleanly for this hosted-schema slice too: the focused red/green proof passes, `dotnet format .\Blueprint.sln --no-restore --include src\Blueprint.Runtime\Http\BlueprintHttpOptions.cs src\Blueprint.Runtime\Http\ApplicationBuilderExtensions.cs src\Blueprint.Runtime\Http\ManifestSchemaInspectionEndpoint.cs src\Blueprint.SourceGenerator.Tests\Artifacts\ManifestInspectionEndpointTests.cs` is clean aside from the repo's known IDE1006 noise, `dotnet build .\Blueprint.sln --no-restore` succeeds, and `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --no-restore` passes with 190 tests green.
- With runtime schema hosting in place, the next approved follow-on shifts to cross-artifact alignment: add a first machine-readable invalidation report for the Tier 2 synthetic scale harness using the shared manifest envelope so performance artifacts start following the same contract.
- That first cross-workstream alignment slice is now complete locally too: `src\Blueprint.SourceGenerator.Tests\Scale\SyntheticFixtureInvalidationReport.cs` emits a `scale-invalidation` envelope with a contributor-owned `synthetic-scale-harness` section, and `SyntheticFixtureInvalidationReportTests` snapshot the single-operation and shared-contract mutation summaries so the manifest contract now covers reviewable performance invalidation artifacts as well as operations inspection surfaces.
- The next cross-workstream alignment slice is now complete locally too: `src\Blueprint.SourceGenerator.Tests\Scale\SyntheticBuildTelemetryReport.cs` emits a sibling `build-telemetry` envelope with a contributor-owned `synthetic-build-harness` section, and `SyntheticBuildTelemetryReportTests` snapshot cold-build plus single-edit synthetic summaries so the manifest contract now also covers reviewable build telemetry metadata without inventing a new envelope family.
- The first repo-native synthetic MSBuild runner is now complete locally too: `migration\performance-and-scale\Capture-SyntheticBuildTelemetry.ps1` emits cold-build, no-op-build, and single-edit-build `.binlog` plus `timing-summary.json` files and writes the reviewed `build-telemetry` envelope for each capture, while `SyntheticBuildTelemetryRunnerTests.cs` proves the manifest contract still roundtrips through `GeneratedManifestEnvelope.FromJson(...)` for those real artifacts.
- The design-time build follow-on is now complete locally too: the same runner emits a `design-time-build` capture that keeps using the reviewed `build-telemetry` envelope with `capture.isDesignTimeBuild = true` instead of splitting into a separate design-time artifact family.
- The next approved alignment follow-on remains contract reuse, not a new artifact family: keep using `GeneratedManifestEnvelope` while the performance workstream broadens into concurrent stress evidence and later benchmark/telemetry publication lanes on top of the same performance artifact contracts.

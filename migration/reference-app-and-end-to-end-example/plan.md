# Reference app and end-to-end example

- Status: In Progress — Layer 1 complete
- Priority: P1
- Classification: Cross-cutting reference surface
- Depends on: Layer 1 depends on `../pipeline-composition-and-execution/plan.md` and `../http-host-and-message-population/plan.md`; the full target then evolves alongside `../performance-and-scale/plan.md`, `../operation-discovery-and-descriptors/plan.md`, `../result-model-and-host-translation/plan.md`, `../validation/plan.md`, `../authentication-and-authorisation/plan.md`, `../auditing-and-observability/plan.md`, `../resource-model-links-and-post-processing/plan.md`, and `../openapi/plan.md`

## Why this module exists

The repository needs a realistic, runnable, inspectable Blueprint app that proves the system can be used end to end by both humans and AI agents.

The current sample now proves the Layer 1 baseline, but it is not yet a complete adoption surface, debugging aid, or migration target:

- `src\Blueprint.SourceGenerator.Sample\SampleBlueprintApp.cs` now scans both local operations and the contract assembly, but the app still needs Layer 2 breadth.
- `src\Blueprint.SourceGenerator.Sample\Startup.cs` now wires logging plus the generated executor into `MapBlueprintApi(...)`, but the sample still lacks richer validation/link/result demonstrations.
- `src\Blueprint.SourceGenerator.Sample\Api\GetTemplatesQueryHandler.cs` and `GetTemplateInlineQueryHandler.cs` now return deterministic in-memory template data.
- `src\Blueprint.SourceGenerator.Tests.Integration\Http\ReferenceAppHttpEndToEndIntegrationTests.cs` now proves the sample routes execute through the generated HTTP host, but manifests and inspection artifacts are still missing.
- `src\Blueprint.SourceGenerator.Sample.Contract\Api\GetTemplatesQuery.cs` and the template resources still represent only the opening slice of the desired templates-domain story.

If we leave the sample in that state, future work will keep arguing in the abstract about API ergonomics, generated manifests, auth, validation, resource links, and OpenAPI without a single concrete app that demonstrates the intended experience.

## Migration goal

Turn the sample into a curated reference app that:

- demonstrates the recommended consumer contract
- exercises multiple Blueprint systems together through one coherent domain
- stays runnable and understandable without external infrastructure
- doubles as a durable integration test surface
- gives future AI sessions a concrete target when implementing or reviewing new capabilities

## Relationship to the performance plan

The reference app is not the large-scale stress fixture.

Its job is to be understandable and realistic. The performance plan owns the synthetic large-project fixtures needed to prove Signature365-scale behavior. The two plans should stay aligned, but they should not collapse into one oversized sample.

## Domain direction

Keep the existing templates domain and evolve it into a small but believable API rather than inventing a completely new example.

The reference app should feel like a genuine slice of a larger product:

- templates exist as first-class resources
- templates have versions or revisions
- templates can be created, updated, previewed, published, archived, and searched
- some operations are read-only and public/internal-view friendly, while others require authentication/authorisation
- validation, links, and result contracts matter in visible ways

That keeps continuity with the current sample while giving the app enough shape to exercise the framework properly.

## Target capability surface

Plan for a curated set of roughly 10-20 operations across a few related resources.

Representative operations:

- list templates
- get template
- create template
- update template metadata
- update template content
- preview template rendering
- list template versions
- publish a template version
- archive or restore a template
- search templates
- optionally clone a template

Representative resources:

- `TemplateApiResource`
- `TemplateListApiResource`
- `TemplateVersionApiResource`
- `TemplatePreviewApiResource`
- focused summary/list resources where that keeps the example readable

The app should show at least:

- descriptor generation and app registration
- HTTP routing and binding
- self/root/related links
- validation failures
- authentication and authorisation
- semantic result handling
- error and failure paths such as validation rejections, not-found/conflict outcomes, and exception translation
- generated tracing/logging hooks
- OpenAPI output derived from generated descriptors
- generated manifests or debug surfaces that explain the registered API

## Project topology

Keep the project structure representative but simple.

Minimum target:

- `Blueprint.SourceGenerator.Sample.Contract` holds public operations, resources, and contract-facing attributes
- `Blueprint.SourceGenerator.Sample` holds handlers, in-memory services, host wiring, and generated execution

Optional later additions:

- a companion project only if it demonstrates an important supported pattern such as shared contracts across more than one host or package-owned generator/runtime extensions

Do not add more projects to the sample unless they explain something the two-project split cannot.

## Runtime support to build

The reference app should remain infrastructure-light by default.

Plan for:

- in-memory repositories/services so the app runs out of the box
- deterministic seeded data for tests and demos
- handlers that show both the preferred default authoring style and at least one secondary supported style where that is important to preserve
- minimal host wiring that reflects the intended post-migration consumer experience rather than legacy builder compatibility

Avoid making a database, queue, or external service a requirement for understanding the sample.

## Contract and authoring guidance

The app should become the canonical place to show the preferred Blueprint authoring story.

That means:

- contract project contains the public operation/resource surface
- host project contains handlers, services, and runtime configuration
- descriptors, links, auth, validation, and result contracts are visible in source rather than hidden behind magic conventions
- legacy compatibility shims should not become the dominant sample style once better generated patterns exist

If a new feature is hard to explain through the reference app, that is often a sign the feature needs a better consumer contract.

## Documentation and inspection surfaces

The reference app should not just run; it should explain itself.

Plan for repo-owned artifacts such as:

- a sample-specific README once the app becomes truly runnable
- generated or snapshot-tested manifests summarizing operations, routes, and links
- OpenAPI output checked as a reviewable artifact
- dev-only inspection endpoints or tooling once the observability plan provides them
- integration tests that make the happy path and a few key failure cases obvious
- a small before/after migration note or artifact later, once migration tooling exists, so one operation can be understood from legacy authoring to generated authoring

The app should help answer:

- what operations exist?
- how are they exposed over HTTP?
- what links are generated?
- what auth or validation applies?
- what semantic results are expected?

## Phase-gated capability rollout

Do not wait until every module is finished before improving the sample, but do expand it in deliberate layers.

### Layer 1 - Make the current sample real

- uncomment/restore the generated executor usage
- replace `NotImplementedException` handlers with deterministic in-memory behavior
- prove end-to-end HTTP execution for the current template queries

Layer 1 is now complete and is the baseline for future reference-app work.

### Layer 2 - Demonstrate the core parity path

- add command + query coverage
- add validation and auth examples
- add richer links and result shapes
- fix or replace any current runtime helper gaps that would make link-based demonstrations misleading, including the current `GetLinkFor(...)` stub in `BlueprintApp`
- add generated inspection artifacts

Start Layer 2 with a validation-focused command and end-to-end proof, then grow the inspection artifacts from that
coherent failure path.

### Layer 3 - Demonstrate the richer API surface

- add OpenAPI as a reviewable artifact
- add resource-event or post-processing examples once the no-nested design is settled
- add any carefully-chosen grouped-handler or package-extension examples that are part of the preferred future authoring story

Each layer should leave the sample runnable and comprehensible.

## Testing role

The reference app should become a first-class test surface, not just a demo.

Use it for:

- end-to-end integration tests
- route/link/OpenAPI snapshots where appropriate
- regression tests for generated manifests and inspection surfaces
- smoke tests for the preferred host wiring

This app is where future changes should prove they feel coherent to an actual consumer, not just to isolated unit tests.

## Observability and debuggability

The reference app should showcase the repository's observability principles in a concrete way.

Plan for it to expose or generate:

- operation/route/link summaries
- trace-friendly operation naming
- representative structured logging paths
- enough metadata that an AI agent can inspect the app surface without reverse-engineering every generated file

If we claim Blueprint is AI-friendly, the reference app should make that claim legible.

## Explicit decisions

- Keep the reference app small enough to read and reason about; do not turn it into the stress harness.
- Prefer one coherent domain over many disconnected mini-examples.
- Keep the app runnable without external infrastructure by default.
- Use the app to show the preferred future authoring style, not to preserve every legacy activation API forever.
- Add more sample surfaces only when the main reference app would otherwise become confusing; do not proliferate many shallow examples too early.

## Sequencing

Start improving the sample as soon as the core HTTP execution path is stable enough to run real handlers.

Keep evolving it alongside the core parity modules so it becomes richer as the architecture matures, and treat it as effectively required before calling the HTTP/auth/validation/OpenAPI story ready for broader consumer migration.

# Compile-time validation

This repository prefers deterministic build-time feedback over runtime surprises.

If the compiler or generator can detect a bad configuration, it should do so with a precise diagnostic instead of letting the runtime discover the problem later.

## Current validation surfaces

### Resource, app, and descriptor shape

- `BP001` comes from `ApiResourceSourceGenerator` and enforces partial resource types when the generator needs to synthesize the `Object` property (`src\Blueprint.SourceGenerator\Generators\ApiResources\ApiResourceSourceGenerator.cs`).
- `BP002`, `BP003`, and `BP004` come from `AppDataModelSourceGenerator` and cover app registration shape and scan issues (`src\Blueprint.SourceGenerator\Generators\App\AppDataModelSourceGenerator.cs`).
- `BP005` comes from `OperationDescriptorSourceGenerator` and enforces required partial types for descriptor generation (`src\Blueprint.SourceGenerator\Generators\Descriptors\OperationDescriptorSourceGenerator.cs`).

### Pipeline and handler validation

`src\Blueprint.SourceGenerator\Generators\Pipelines\DiagnosticDescriptors.cs` defines the main pipeline diagnostics:

| Range | Meaning |
| --- | --- |
| `BP100`-`BP199` | hard errors around missing handlers, duplicate handlers, invalid signatures, and unsupported shapes |
| `BP200`-`BP299` | warnings or info around performance and ergonomics, such as async usage or static handlers |
| `BP300`-`BP399` | informational diagnostics that can help inspect generator behavior |

These diagnostics should stay stable and descriptive because they are part of the repo's human and AI feedback loop.

### Migration and refactoring diagnostics

Large-scale migrations should use a dedicated diagnostic range rather than mixing mechanically-fixable migration findings into the ordinary generator IDs.

Planned direction:

| Range | Meaning |
| --- | --- |
| `BPMIG001`-`BPMIG099` | auto-fixable migration diagnostics |
| `BPMIG100`-`BPMIG199` | configuration-required migration findings |
| `BPMIG200`-`BPMIG299` | consumer-extension rewrites or custom migration work |
| `BPMIG300+` | manual design-review items that should not be silently auto-fixed |

Migration diagnostics should be paired with machine-readable reporting such as SARIF so that larger refactors remain reviewable and automatable.

### Link and route validation

`LinkGenerationModule` defines `BPLG001` through `BPLG003` (`src\Blueprint.SourceGenerator\Modules\LinkGeneration\LinkGenerationModule.cs`):

- placeholder-to-property validation for route templates
- duplicate URL detection
- resource-type compatibility checks for linkable resources

This is the clearest current example of the repository principle that string literals should be validated against symbols when possible.

## Diagnostic design rules

When adding new diagnostics:

1. Validate the contract at the earliest stage that has enough information.
2. Point at the most useful source location, not just the containing type.
3. Include concrete type, member, route, or configuration names in the message.
4. Make the fix path obvious from the message or description.
5. Choose severity intentionally. If generation would be wrong or unsafe, fail the build.

For migration diagnostics specifically:

6. Be explicit about whether the finding is auto-fixable, configuration-required, or manual-review-only.
7. Point to the relevant migration plan when the fix is not fully mechanical.
8. Prefer reports that can be consumed by both CI and AI agents without log scraping.

Good diagnostics here should help both humans and AI agents repair the issue quickly.

## Snapshot and cacheability tests are part of the harness

`TestHelpers.Compile<T>()` is not just a snapshot helper (`src\Blueprint.SourceGenerator.Tests\TestHelpers.cs`).

It does three important things:

- compiles the generated output to prove it is valid
- captures generator output for snapshot review
- reruns the incremental generator and asserts that the second run is fully cached

That means a generator change can be "textually correct" but still wrong if it breaks cacheability or emits unexpected diagnostics.

When adding a new validation rule, add or update the matching deterministic tests:

- unit tests for the diagnostic or helper behavior
- snapshot tests when generated output changes
- cacheability-sensitive tests when the generator pipeline changes

Those tests should prove behavior through supported seams. Do not use reflection in tests to assert that a contract exists or to invoke private/internal helpers. If a behavior is hard to verify without doing that, improve the public or otherwise supported seam first and then test through that seam.

For new planning helpers and registries, do not stop at a single happy-path assertion. Cover:

- the expected happy path
- the failure or diagnostic path
- determinism edge cases such as duplicate producers, dependency cycles, ordering ties, or ambiguous metadata

## What should be validated at compile time

The preferred targets for future validation include:

- route-template placeholders and mapped resource properties
- marker-attribute arguments
- contributor composition rules such as duplicate unique contributors, missing contributor dependencies, and cyclic contributor ordering
- serializer and `JsonSerializerContext` completeness for API-facing contracts
- handler parameter resolution rules
- generated metadata or manifest completeness
- inspection-surface consistency for large APIs

If a feature depends on a string, type name, metadata shape, or serializer contract that is knowable during generation, the long-term goal is to validate it before the app runs.

The foundation module system now reserves `BPMOD001`-`BPMOD004` for contributor-composition and pipeline-planning diagnostics:

- duplicate unique contributors
- missing contributor dependencies
- cyclic contributor dependency graphs
- missing required pipeline variables discovered during pipeline-plan validation, including values that only become available after the consuming contributor stage

These are compile-time failures because non-deterministic module composition or unresolved required pipeline variables would make generated output unstable and hard to inspect. `BPMOD004` is stage-aware: a matching provision must exist and be available no later than the consuming contributor's declared required stage. Recent pipeline-contract cleanup intentionally removed the separate orphan-render-step diagnostic by making stage participation implicit in the planning helpers instead of leaving it as caller-managed bookkeeping.

## Human- and AI-friendly diagnostics

Diagnostics in this repo should be:

- stable, with meaningful IDs
- precise about the failing contract
- easy to correlate back to source and generated output
- specific enough that a tool can act on them without guesswork

Avoid vague messages like "invalid configuration" when the build can say "property `UserId` does not exist on operation `CreateUserCommand`".

## Preferred escalation path

When a repeated class of bug appears:

1. first ask whether a diagnostic should exist
2. then ask whether a test should prove it
3. then update `/docs` so the principle and harness are both documented

The repository should become harder to misuse over time.

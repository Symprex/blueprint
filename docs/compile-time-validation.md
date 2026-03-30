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

Recent pipeline-contract coverage in that range includes `BP105` for nullable plain-value handler returns, `BP106` for unsupported `Results<T1, T2>` member types, `BP107` for handler returns that do not match the declared operation result contract, `BP108` for host-exposed semantic result contracts that the current enabled host executor set cannot translate, `BP109` for host-exposed semantic result unions whose supported members still collapse to the same current host response shape, `BP110` for host-exposed semantic result unions that still share the same HTTP status code without an explicit host/client discrimination strategy, `BP111` for `Results<T1, T2>` contracts whose flattened descriptor outcomes would drift away from the declared semantic member set, and `BP112` for HTTP-visible unions that try to mix host-neutral `NoResultOperationResult` with host-owned no-content/status-only result families such as `HttpNoContentResult`. The current HTTP executor surface now also includes the host-neutral `CreatedResult<T>` semantic shape, which maps to body-bearing `201 Created` responses, the host-neutral `AcceptedResult<T>` semantic shape, which maps to body-bearing `202 Accepted` responses, and the host-neutral `KeyOnlyResourceEventResult` semantic shape, which maps to empty `201 Created` responses by convention. Keep those rules compile-time-first: the generator should reject unsupported or ambiguous result shapes before runtime execution needs to guess how to normalize them, and the compile-time response-shape rules should preserve the deliberate distinction between `CreatedResult<T>` (`201` with body), `AcceptedResult<T>` (`202` with body), `KeyOnlyResourceEventResult` (`201` empty), `NoResultOperationResult` (`200` empty), and `HttpNoContentResult` (`204` empty).

### Migration and refactoring diagnostics

Large-scale migrations should use a dedicated diagnostic range rather than mixing mechanically-fixable migration findings into the ordinary generator IDs.

Current reservation:

| Range | Meaning |
| --- | --- |
| `BPMIG001`-`BPMIG099` | auto-fixable migration diagnostics |
| `BPMIG100`-`BPMIG199` | configuration-required migration findings |
| `BPMIG200`-`BPMIG299` | consumer-extension rewrites or custom migration work |
| `BPMIG300`-`BPMIG399` | manual design-review items that should not be silently auto-fixed |

The migration analyzer seam now lives in `src\Blueprint.Migration.Analyzers\MigrationDiagnosticDescriptors.cs`, with analyzer proof in `src\Blueprint.Migration.Analyzers.Tests` and code-fix implementation in `src\Blueprint.Migration.Analyzers.CodeFixes`. The live auto-fixable activation diagnostics now include `BPMIG001` for redundant parameterless `BlueprintApiBuilder.AddOpenApi()` calls, `BPMIG002` for redundant parameterless `AddHateoasLinks()` calls, and `BPMIG003` for redundant parameterless `AddAuthorisation()` calls, all of which describe capabilities that the source-generated model should infer without an activation switch. The configuration-required companion rules now start with `BPMIG100` for configured `AddOpenApi(Action<OpenApiOptions>)` usage and `BPMIG101` for configured `AddAuthentication(...)` usage, both of which preserve consumer-owned setup work instead of treating it as a safe auto-fix. The consumer-extension rewrite rules now also include `BPMIG200` for legacy `[FromResource(...)]` usage and `BPMIG201` for raw `{Auth:...}` placeholder usage, so both auth/resource inheritance and auth-template expansion work enter the shared analyzer/report/codemod flow before descriptor lowering automation exists. The structured report contract for that family now lives in `src\Blueprint.Migration.Analyzers\MigrationReport.cs` and `src\Blueprint.Migration.Analyzers\DiagnosticFinding.cs`: findings are ordered deterministically by diagnostic ID, classified into the same planning buckets (`auto-fixable`, `configuration required`, `consumer-extension rewrite required`, `manual design review required`), point at the durable migration-plan path for the owning workstream, and are now guarded by explicit JSON contract tests that validate the required top-level and finding-level payload shape for `migration-diagnostics`. The repo-native codemod seam now spans `src\Blueprint.Codemod\SolutionAnalysisRunner.cs` for dry-run SARIF collection, `src\Blueprint.Codemod\SarifDiagnosticReader.cs` for explicit SARIF input validation, `src\Blueprint.Codemod\TransformationApplier.cs` plus `ApplyModeOptions.cs` for the first apply-mode foundation, and `src\Blueprint.Codemod\CodemodArtifactWriter.cs` plus `CodemodMarkdownSummaryWriter.cs` for deterministic `codemod-report.json` / `codemod-report.md` emission through `--output <dir>`. Supported migration fixes (currently `BPMIG001`) are replayed through the existing analyzer code-fix provider inside an `MSBuildWorkspace`, repeat runs stay idempotent, malformed dry-run SARIF now fails loudly when required `runs`, `results`, or string `ruleId` fields are missing, non-auto-fixable findings such as `BPMIG100`, `BPMIG101`, `BPMIG200`, and `BPMIG201` remain in the post-apply report rather than being silently rewritten, and all artifact projections stay sourced from the canonical `MigrationReport` payload. Future migration diagnostics should extend that shared analyzer/test/report/codemod seam rather than introducing ad hoc project layouts.

Migration diagnostics should be paired with machine-readable reporting such as SARIF and the repo-native JSON report object model so that larger refactors remain reviewable and automatable without log scraping.

### Link and route validation

`LinkGenerationModule` defines `BPLG001` through `BPLG003` (`src\Blueprint.SourceGenerator\Modules\LinkGeneration\LinkGenerationModule.cs`):

- placeholder-to-property validation for route templates
- duplicate URL detection
- resource-type compatibility checks for linkable resources

This is the clearest current example of the repository principle that string literals should be validated against symbols when possible.

### Operation discovery and host diagnostics

Operation-discovery and host-exposure checks should use their own stable range instead of borrowing link or validation IDs.

Reserved direction:

| Range | Meaning |
| --- | --- |
| `BPOD001`-`BPOD099` | operation-discovery and host-exposure diagnostics for descriptor/app-registration contracts that the generator can prove at build time |

Current operation-discovery IDs in active use:

- `BPOD001`: descriptor planning recorded host exposure that no enabled host contributor supports.
- `BPOD002`: descriptor planning recorded exposure to more than one enabled host contributor for the same operation.

### Validation contributor diagnostics

Validation-oriented compile-time checks should use a dedicated range instead of borrowing the link or foundation IDs.

Reserved direction:

| Range | Meaning |
| --- | --- |
| `BPV001`-`BPV099` | validation contributor diagnostics for invalid declarative contracts that the generator can prove at build time |

Typical examples include validation attributes that name other members with strings, such as `CompareAttribute.OtherProperty` pointing at a property that does not exist on the operation type. When those contracts are knowable during generation, prefer a build-time diagnostic over waiting for runtime validation to fail indirectly.

Current validation contributor IDs in active use:

- `BPV001`: `CompareAttribute` references a property that does not exist on the operation type.
- `BPV002`: `GreaterThanPropertyAttribute` references a property that does not exist on the operation type.
- `BPV003`: `RequiredIfAttribute` references a property that does not exist on the operation type.
- `BPV004`: `RequiredIfNotAttribute` references a property that does not exist on the operation type.
- `BPV005`: `NotAllowedIfAttribute` references a property that does not exist on the operation type.

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
- runtime integration tests when the generated pipeline behavior changes and the runtime seam is executable in `Blueprint.SourceGenerator.Tests.Integration`

For pipeline contributor work, do not stop at a line-presence assertion in generated code. The default proof set is:

- a snapshot of the full generated pipeline shape
- compile-time failure coverage for invalid symbol-backed declarative contracts
- runtime integration coverage that executes the generated pipeline for both valid and invalid paths

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

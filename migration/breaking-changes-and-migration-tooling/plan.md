# Breaking changes and migration tooling

- Status: Planned
- Priority: P0
- Classification: Cross-cutting rollout
- Depends on: `../foundation-module-system/plan.md` for initial strategy; analyzer and code-fix details then evolve alongside operation and pipeline design

## Why this module exists

The rewrite is allowed to make deliberate breaking changes where the new source-generated model is substantially better, but that only works if migration is mechanical and well-instrumented.

Without explicit migration tooling, we would trade legacy runtime complexity for consumer pain, documentation debt, and high-friction upgrades across Symprex applications.

## Evidence

- Widespread legacy activation usage across Signature365 HTTP applications: `.AddAuthentication(...)`, `.AddAuthorisation()`, and `.AddOpenApi(...)`
- Partial usage of `.AddHateoasLinks()`, showing that some current activation APIs represent optional customization rather than a capability that should be architecturally absent
- `D:\Projects\Signature365\src\Core\Symprex.Signature365.Common.Core\Authorisation\FromResourceAttribute.cs` plus usage scan showing `[FromResource(...)]` appears 154 times
- Usage scan showing `{Auth:...}` placeholders appear 140 times
- `D:\Projects\Signature365\src\Blueprint\Blueprint.Http\Middleware\ResourceEventHandler.cs` showing the current nested-execution trigger is resource-event self-query rather than a broad public feature

## Migration goal

Make every deliberate breaking change explicit, classify whether it is analyzer-fixable or documentation-guided, and ship automated tooling so the legacy Blueprint-to-source-generated transition is mostly mechanical.

## Recommended consumer-app migration workflow

The canonical execution workflow for migrating a Signature365 app lives in `docs\refactoring-workflow.md`.

This plan owns:

- why the migration is allowed to break source/runtime shape
- what kinds of changes should be analyzer-fixable versus documentation-guided
- what tooling and artifact classes must exist

The workflow document owns the step-by-step execution runbook, commands, and artifact locations.

Required per-app artifacts should include:

- legacy baseline manifests and OpenAPI output before changes
- migration SARIF and codemod reports during the rewrite
- generated/manifests/OpenAPI/equivalence reports after cutover

The migration is not complete until the equivalence review passes and the temporary shims can be removed.

## Breaking changes currently expected

### 1. Activation APIs become configuration APIs

Likely changes:

- `AddOpenApi()` stops being the way OpenAPI appears; HTTP implies OpenAPI
- `AddAuthentication()` and `AddAuthorisation()` stop being feature toggles and become configuration hooks or disappear
- `AddHateoasLinks()` stops being required to turn on link generation when link metadata already exists

### 2. General nested execution is removed or narrowed heavily

Likely changes:

- no public/general `ExecuteNestedAsync` parity story
- resource-event self-query is redesigned first
- any surviving internal re-query path is narrow and not part of the ordinary extension surface

### 3. Runtime scanner/builder extension points give way to compile-time modules

Likely changes:

- runtime scanner-style hooks are replaced by source-generator modules and descriptor/pipeline contributors
- `ApiOperations` / grouped handler discovery moves into core rather than remaining a consumer add-on
- consumer customization prefers generator packages over runtime frame injection

### 4. Resource/auth magic is reduced

Likely changes:

- `{Auth:...}` and property-template expansion move into compile-time-generated code
- `[FromResource(...)]` becomes compile-time-resolved metadata
- hidden access-mode and any-of permission semantics become explicit generated auth metadata instead of being implied by runtime authoriser behavior
- `SetTenantMiddleware`-style behavior becomes generated property hydration where possible
- DB-backed ownership/resource expansion remains consumer-space rather than core Blueprint behavior
- convention-style auth completeness checks and one-fixture-per-operation patterns give way to analyzers, generated manifests, and parameterized test scaffolding

### 5. Validation surface is trimmed

Likely changes:

- DataAnnotations becomes the primary validation contract
- only a focused Blueprint-specific validator subset remains strategic
- low-value legacy validators become compatibility aliases or migration targets

### 6. Integration boundaries become stricter

Likely changes:

- Rebus and Marten stop being implied future core packages
- Blueprint provides seams; consumers or companion packages provide the concrete integrations

### 7. `ApiOperations` / `OperationsHandlerScanner` moves into core

Likely changes:

- the grouped-handler discovery story moves from Symprex consumer-space into core Blueprint
- Symprex packages may need a compatibility wrapper or forwarding type during transition
- migration tooling must detect old references and rewrite them to the new core shape where possible

This move should be treated as a precondition for the main operation-discovery and pipeline-composition implementation work, because both plans assume grouped-handler discovery is already part of core Blueprint rather than an external add-on.

## Tooling to build

### Roslyn analyzers and code fixes

Plan a `Blueprint.Migration.Analyzers` package or equivalent that can:

- flag legacy activation calls that no longer match the target architecture
- remove or replace builder calls mechanically where the new behavior is defaulted
- flag unsupported nested-execution assumptions
- identify legacy validation attributes that should migrate to DataAnnotations or custom consumer validators
- validate `[FromResource(...)]`, resource templates, and auth placeholder usage against the new compile-time rules
- detect operations whose legacy auth story depends on implicit access-mode or template-expansion behavior that the new generator makes explicit
- rewrite or flag legacy `OperationsHandlerScanner` / grouped-handler references as that behavior moves into core Blueprint

Recommended libraries:

- `Microsoft.CodeAnalysis.CSharp`
- `Microsoft.CodeAnalysis.CSharp.Workspaces`
- `Microsoft.CodeAnalysis.Analyzers`
- Roslyn analyzer/code-fix test packages for deterministic diagnostics and fix coverage

Preferred execution flow:

- `dotnet build` to surface migration diagnostics and emit SARIF
- `dotnet format --diagnostics ...` to apply safe code fixes at solution scale
- a second `dotnet build` to confirm only manual-review or configuration-required findings remain

### Workspace codemod CLI

Plan a `Blueprint.Codemod` tool or equivalent for migrations that are too large or stateful for a normal code fix.

Recommended libraries:

- `Microsoft.Build.Locator`
- `Microsoft.CodeAnalysis.Workspaces.MSBuild`
- `Microsoft.CodeAnalysis.CSharp.Workspaces`
- `System.CommandLine`

Required capabilities:

- solution loading without IDE dependence
- dry-run mode
- machine-readable report output
- human-readable summary output
- apply mode followed by normal build/test/format validation

### Upgrade report / manifest

Provide a migration report that classifies findings into:

- auto-fixable
- configuration required
- consumer-extension rewrite required
- manual design review required

The report should be readable by both humans and AI agents and point to the relevant migration documentation.

Preferred artifacts:

- SARIF for diagnostics
- JSON for codemod/refactor reports
- a short markdown summary for quick review
- before/after app manifests and equivalence reports for migrated consumer apps

### Compatibility shims

Where useful, ship short-lived compatibility shims that:

- keep source compatibility long enough for analyzers/code fixes to run
- emit targeted diagnostics explaining the preferred replacement
- avoid silently preserving legacy runtime behavior that the new architecture deliberately removes

Each shim should also declare:

- the migration diagnostic or analyzer family that replaces it
- the intended replacement API or pattern
- the explicit removal gate, for example "remove after the first analyzer-led migration release and at least one Symprex app has completed the cutover path"

## Consumer configuration and activation

Migration tooling should assume the end state is:

- compile-time capability discovery and generation first
- explicit configuration only for non-inferable behavior
- consumer-owned generator packages for deep extension scenarios

The tooling should therefore prefer deleting unnecessary activators over inventing new ones.

## Release strategy

Plan the rollout as an analyzer-led major-version change:

- ship analyzers and migration reports early so applications can clean up usage before the final cutover
- batch the deliberate source-breaking changes into a clearly signposted major version of the source-generated Blueprint stack
- allow temporary compatibility shims in Symprex-owned packages only where they reduce migration friction without preserving unwanted runtime architecture long-term

## Compile-time checks to add

- legacy activation APIs used in ways that no longer have architectural meaning
- consumer code still depending on general nested-execution concepts
- resource/auth templates that cannot be satisfied by compile-time generation
- consumer-specific integrations that need a package/module but only have runtime middleware today
- validation attributes that no longer belong in the supported Blueprint subset

## Current migration status

- The `BPMIG001`-`BPMIG399` range is now reserved in repo guidance, `Blueprint.Migration.Analyzers`, `Blueprint.Migration.Analyzers.CodeFixes`, and `Blueprint.Migration.Analyzers.Tests` provide the first complete analyzer/code-fix/test seam, `BPMIG001` now flags plus safely removes redundant parameterless `BlueprintApiBuilder.AddOpenApi()` activation calls, `BPMIG002` now flags redundant parameterless `AddHateoasLinks()` activation calls, `BPMIG003` now flags redundant parameterless `AddAuthorisation()` activation calls, `BPMIG100` now flags configured `AddOpenApi(Action<OpenApiOptions>)` usage as configuration-required migration work, `BPMIG101` now flags configured `AddAuthentication(...)` usage as configuration-required migration work, `BPMIG200` now flags legacy `[FromResource(...)]` usage, and `BPMIG201` now flags raw `{Auth:...}` placeholder usage.
- `src\Blueprint.Migration.Analyzers\MigrationReport.cs`, `src\Blueprint.Migration.Analyzers\DiagnosticFinding.cs`, and `src\Blueprint.Migration.Analyzers.Tests\MigrationReportSerializationTests.cs` now define the shared repo-native JSON report contract, including `SchemaVersion = 1`, `ReportType = "migration-diagnostics"`, deterministic diagnostic ordering, migration-bucket classification, and durable documentation-path links for both activation and auth/resource findings.
- `src\Blueprint.Codemod\Blueprint.Codemod.csproj`, `src\Blueprint.Codemod\SolutionAnalysisRunner.cs`, and `src\Blueprint.Codemod.Tests\ActivationDiagnosticsReportCollectionTests.cs` now provide the first repo-native dry-run collector for that contract: a solution path can be built with a temporary SARIF error log, filtered down to the BPMIG family, and projected back into the shared `MigrationReport` payload, including the first auth/resource findings.
- `src\Blueprint.Codemod\ApplyModeOptions.cs`, `src\Blueprint.Codemod\TransformationApplier.cs`, and `src\Blueprint.Codemod.Tests\ApplyModeTests.cs` now add the first apply-mode foundation: the codemod opens a solution through `MSBuildWorkspace`, reuses the dry-run planning/filter seam, replays the supported migration code-fix providers (currently `BPMIG001`), and proves repeat-run idempotency while leaving configuration-required findings in the post-apply report.
- `src\Blueprint.Codemod\CodemodArtifactWriter.cs`, `src\Blueprint.Codemod\CodemodMarkdownSummaryWriter.cs`, `src\Blueprint.Codemod\Program.cs`, and `src\Blueprint.Codemod.Tests\CodemodArtifactWriterTests.cs` now complete the first durable artifact-output slice: dry-run and apply runs can keep writing the canonical JSON report to stdout while `--output <dir>` also emits reviewable `codemod-report.json` and `codemod-report.md` files sourced from the same `MigrationReport`.
- `src\Blueprint.Migration.Analyzers.Tests\MigrationReportSerializationTests.cs` and `src\Blueprint.Codemod.Tests\CodemodArtifactWriterTests.cs` now also carry explicit schema-style JSON contract assertions for the shared `migration-diagnostics` payload, so required top-level and finding-level properties are guarded against drift without adding a new third-party dependency.
- `src\Blueprint.Codemod\SarifDiagnosticReader.cs`, `src\Blueprint.Codemod\SolutionAnalysisRunner.cs`, and `src\Blueprint.Codemod.Tests\SarifDiagnosticReaderTests.cs` now add the matching SARIF contract guardrail for the dry-run input artifact: the codemod uses a supported reader seam that keeps rule IDs sorted and distinct while rejecting missing `runs`, malformed `results`, or non-string `ruleId` values before the shared migration report can silently drop findings.
- Next approved slice: add dry-run idempotency tests for the codemod CLI so repeated review-only runs prove stable outputs before broader rollout.

## Observability and debuggability

- per-project migration manifest
- list of automated fixes applied versus remaining manual actions
- diagnostics that explain *why* an API changed, not just *that* it changed
- cross-links from diagnostics into these migration plans and future upgrade guidance
- documented commands and artifact locations in `docs\refactoring-workflow.md`
- explicit before/after artifacts that let reviewers compare a migrated app against its legacy baseline

## Explicit decisions

- Breaking changes are acceptable when they materially improve determinism, AOT-friendliness, and long-term maintainability.
- Mechanical migrations are preferable to preserving low-value legacy APIs indefinitely.
- Migration analyzers/code fixes are part of the deliverable, not optional polish.
- Migration reports must help both humans and AI agents understand unresolved blockers quickly.
- Large structural refactors should default to Roslyn-aware analyzers/code fixes or a workspace codemod CLI, not undocumented regex/manual batch editing.
- The detailed execution workflow belongs in `docs\refactoring-workflow.md`; this plan owns the architectural and rollout side.

## Sequencing

Start this work alongside the foundation design. The analyzer/code-fix surface can ship incrementally as architectural decisions become stable, but the migration story must be planned from the start rather than after implementation.

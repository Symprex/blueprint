# Refactoring workflow

Use this document when a change is large enough that manual edits or one-off search/replace would be risky.

The goal is to make large-scale refactorings deterministic, reviewable, and repeatable by both humans and AI agents.

For the concrete consumer-app migration path from legacy Blueprint in Signature365 to the source-generated stack in this repo, see `Legacy Blueprint app migration workflow` below.

## Choose the right layer

| Change shape | Default toolchain | Why |
| --- | --- | --- |
| Single symbol rename or tightly scoped refactor inside this repo | IDE/Roslyn rename + `dotnet build` + `dotnet format` | Symbol-aware, fast, and already covered by the normal build/test loop |
| One-to-one API migration or repeated structural rewrite across a solution | Roslyn analyzers + code fixes | Safe, reviewable, and can expose stable diagnostic IDs plus `FixAll` support |
| Large structural migration across many projects or consumer solutions | Workspace-based codemod CLI + analyzer verification | Lets us dry-run, emit reports, and apply deterministic rewrites without relying on IDE-only workflows |

If a refactor would be unsafe with plain rename/code fix, move up a layer. Do not fall back to regex-first edits for C# structure unless the change is truly trivial and the output is still fully validated afterward.

## Primary tools and libraries

### Layer 1 - IDE rename and repository harness

Use:

- Visual Studio / Rider / Roslyn symbol rename
- `dotnet build`
- `dotnet format`
- existing generator tests, integration tests, and snapshot tests

This layer is appropriate when the repo's normal build, snapshot, and formatting loop is enough to prove the refactor.

### Layer 2 - Roslyn analyzers and code fixes

Current projects:

- `src\Blueprint.Migration.Analyzers`
- `src\Blueprint.Migration.Analyzers.CodeFixes`

Current baseline libraries:

- `Microsoft.CodeAnalysis.CSharp`
- `Microsoft.CodeAnalysis.CSharp.Workspaces`
- `Microsoft.CodeAnalysis.Analyzers`
- `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit`
- `Microsoft.CodeAnalysis.CSharp.CodeFix.Testing.XUnit`

Use this layer when:

- the migration is mostly mechanical
- a stable diagnostic ID should exist
- a `FixAll`-style rewrite is safer than manual edits
- the repo or consuming apps need analyzer-guided upgrades

### Layer 3 - Workspace-based codemod CLI

Planned project:

- `src\Blueprint.Codemod`

Planned libraries:

- `Microsoft.Build.Locator`
- `Microsoft.CodeAnalysis.Workspaces.MSBuild`
- `Microsoft.CodeAnalysis.CSharp.Workspaces`
- `System.CommandLine`

Use this layer when:

- the change spans many projects or solutions
- the rewrite needs a dry-run mode
- the refactor should emit a structured migration report
- the change is too large or stateful for a simple code fix

### Shared validation and reporting tools

Use across layers:

- `dotnet format`
- `dotnet build`
- `dotnet test`
- `Verify` snapshot tests for generated artifacts and reports
- SARIF reports for machine-readable diagnostics

## How to run each layer

## Layer 1 - Local symbol-safe refactors

Typical flow:

```powershell
dotnet build .\Blueprint.sln
dotnet format .\Blueprint.sln --no-restore
dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj
dotnet test .\src\Blueprint.SourceGenerator.Tests.Integration\Blueprint.SourceGenerator.Tests.Integration.csproj
```

Use this for:

- safe renames
- local refactors that do not need a migration report
- changes already well-covered by the existing build and snapshot harness

## Layer 2 - Analyzer-led migrations

With the initial scaffold in place, the target flow is:

```powershell
dotnet build .\Blueprint.sln /p:ErrorLog=artifacts\migration-report.sarif;version=2
dotnet format .\Blueprint.sln --no-restore --diagnostics BPMIG001 BPMIG002
dotnet build .\Blueprint.sln /p:ErrorLog=artifacts\migration-report-after-fix.sarif;version=2
```

Current state and expectations:

- `dotnet build` surfaces live migration diagnostics
- `dotnet format --diagnostics ...` applies safe code fixes where supported once those fixes land
- a second build should eventually leave only configuration-required or manual-review findings

Today the live auto-fixable activation diagnostics are `BPMIG001` for redundant parameterless `AddOpenApi()`, `BPMIG002` for redundant parameterless `AddHateoasLinks()`, and `BPMIG003` for redundant parameterless `AddAuthorisation()`. `BPMIG001` is also the first live analyzer/code-fix pair, while the configuration-required companion rules now include `BPMIG100` for configured `AddOpenApi(Action<OpenApiOptions>)` and `BPMIG101` for configured `AddAuthentication(...)`. The shared consumer-extension rewrite rules now also include `BPMIG200` for legacy `[FromResource(...)]` usage and `BPMIG201` for raw `{Auth:...}` placeholder usage. The first repo-native structured JSON contract for that shared migration family now lives in `src\Blueprint.Migration.Analyzers\MigrationReport.cs` and `src\Blueprint.Migration.Analyzers\DiagnosticFinding.cs`, which serialize the stable `migration-diagnostics` report type, deterministic diagnostic ordering, shared migration buckets, and durable documentation-path links. `src\Blueprint.Codemod` now provides both the first dry-run collector for that contract and the first apply-mode foundation: `SolutionAnalysisRunner` builds a solution with temporary SARIF output to project matching diagnostics into the shared report, while `TransformationApplier` reuses the same filter/report seam inside `MSBuildWorkspace` to replay the currently supported migration code fixes and prove idempotent repeat runs. Artifact emission and markdown summary output are now part of that same shared workflow surface.

## Layer 3 - Codemod CLI

The first repo-native `Blueprint.Codemod` dry-run and apply-mode seams now exist for shared migration-diagnostics work:

```powershell
dotnet run --project .\src\Blueprint.Codemod -- --solution .\Blueprint.sln --diagnostic-filter BPMIG*
dotnet run --project .\src\Blueprint.Codemod -- --solution .\Blueprint.sln --diagnostic-filter BPMIG* --apply
```

The dry-run path shells out to `dotnet build /p:ErrorLog=...`, filters the `BPMIG*` activation family, and writes the shared `MigrationReport` JSON payload to standard output. The first apply-mode path now opens the solution through `MSBuildWorkspace`, reuses the same diagnostic filter, and replays the supported activation code-fix providers (currently the `BPMIG001` cleanup) before emitting the post-apply report to standard output. Markdown summaries, artifact-output directories, and broader transformation routing are still follow-on work.

Large refactors should support reviewable dry-run and apply commands before the richer artifact layer arrives:

```powershell
dotnet run --project .\src\Blueprint.Codemod -- --solution .\Blueprint.sln --diagnostic-filter BPMIG*
dotnet run --project .\src\Blueprint.Codemod -- --solution .\Blueprint.sln --diagnostic-filter BPMIG* --apply
dotnet format .\Blueprint.sln --no-restore
dotnet build .\Blueprint.sln
```

The dry-run must be reviewable before apply. Applying a codemod without a dry-run report should be the exception, not the default.

## CI verification mode

Large-scale refactors should have a CI-friendly verify path:

- restore tools
- build with migration diagnostics enabled
- run `dotnet format` in verify mode where appropriate
- run the relevant test and snapshot suites
- publish the migration/codemod artifacts

Illustrative commands:

```powershell
dotnet tool restore
dotnet build .\Blueprint.sln /p:ErrorLog=artifacts\migration-report.sarif;version=2
dotnet format .\Blueprint.sln --no-restore --verify-no-changes
dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj
dotnet test .\src\Blueprint.SourceGenerator.CodeWriter.Tests\Blueprint.SourceGenerator.CodeWriter.Tests.csproj
dotnet test .\src\Blueprint.SourceGenerator.Tests.Integration\Blueprint.SourceGenerator.Tests.Integration.csproj
```

## Required outputs for larger refactors

Prefer structured outputs first and human-readable summaries second.

Expected artifacts:

- `artifacts\migration-report.sarif`
- `artifacts\migration-report-after-fix.sarif`
- `artifacts\migration-summary.md`
- `artifacts\codemod-report.json`
- `artifacts\codemod-report.md`
- reviewable source and snapshot diffs in Git

Generated reports should classify findings using the same buckets used in the migration planning:

- auto-fixable
- configuration required
- consumer-extension rewrite required
- manual design review required

The current repo-native migration-diagnostics seam follows that same bucket vocabulary and keeps report ordering deterministic by diagnostic ID. `Blueprint.Codemod` now proves that both a real dry-run collector and the first supported apply-mode mutation can reuse that contract from a solution build, `--output <dir>` writes the same canonical report as durable `codemod-report.json` plus a deterministic `codemod-report.md` summary for review and CI pickup without changing the existing stdout JSON behavior, the shared report shape is guarded by explicit JSON-contract tests, the dry-run SARIF input now fails loudly when required `runs`, `results`, or string `ruleId` fields are missing instead of silently dropping findings, and repeated dry-run executions against the same fixture copy are now explicitly proven to keep stdout, durable artifacts, and source files stable before broader rollout.

## Legacy Blueprint app migration workflow

This is the default workflow for moving a single Signature365 app from the current legacy Blueprint stack to the source-generated Blueprint stack.

### Phase 0 - Baseline capture

Before touching code, capture the legacy contract and behavior for the app being migrated.

Expected artifacts:

- `artifacts\<App>\legacy-app-manifest.json`
- `artifacts\<App>\legacy-openapi.json`
- `artifacts\<App>\legacy-smoke-baseline.json`
- `artifacts\<App>\legacy-usage-report.md`

Baseline capture should include:

- operations, routes, and HTTP methods
- auth/resource usage such as `[FromResource(...)]` and `{Auth:...}`
- validation surface
- OpenAPI output
- representative smoke cases for important operations
- any committed `Internal\Generated\Blueprint\` output that helps preserve current behavior during comparison

### Phase 1 - Scan and report

Run analyzers and the codemod CLI in dry-run mode first.

Illustrative codemod command:

```powershell
dotnet run --project .\src\Blueprint.Codemod\Blueprint.Codemod.csproj -- --solution .\Blueprint.sln --diagnostic-filter BPMIG* --output .\artifacts\MyApp
```

Expected artifacts:

- `artifacts\<App>\migration-report.sarif`
- `artifacts\<App>\codemod-report.json`
- `artifacts\<App>\codemod-report.md`
- `artifacts\<App>\migration-summary.md`

The codemod JSON and markdown artifacts should be treated as projections of the same canonical `MigrationReport` payload. Reviewers should be able to diff either file and see the same set of findings, buckets, and linked migration-plan paths.

At the end of this phase, every finding should be classified as:

- auto-fixable
- configuration required
- consumer-extension rewrite required
- manual design review required

### Phase 2 - Apply safe auto-fixes

Apply only mechanical rewrites that are safe enough to run without human interpretation.

Typical examples:

- activation API cleanup
- namespace/type moves
- straightforward bootstrap rewrites
- formatting and import normalization

After apply:

- rebuild
- rerun the migration diagnostics
- confirm only manual-review or configuration-required items remain

### Phase 3 - Manual migration in dependency order

Resolve the remaining items in the same dependency order used by the main migration plans, rather than changing unrelated areas opportunistically.

Typical order for an app migration:

1. app bootstrap and operation discovery
2. pipeline/execution and result handling
3. auth and validation behavior
4. resource/link/resource-event behavior
5. OpenAPI and inspection surfaces
6. consumer-specific integrations and ownership/resource resolvers

Use compatibility shims only where they reduce migration friction without hiding the target architecture.

### Phase 4 - Generated stack cutover

Switch the app fully to the generated descriptors, app registration, and executor path.

Success at this phase means:

- the app builds against the source-generated Blueprint stack
- generated operation registration owns the app surface
- the runtime uses generated executor pipelines instead of legacy runtime discovery

### Phase 5 - Equivalence verification

Compare the migrated app against the baseline captured in Phase 0.

Expected artifacts:

- `artifacts\<App>\generated-app-manifest.json`
- `artifacts\<App>\generated-openapi.json`
- `artifacts\<App>\contract-diff.json`
- `artifacts\<App>\equivalence-report.md`

Minimum gates:

- build is clean
- no unresolved `BPMIG*` diagnostics except documented waivers
- route/method surface matches or intentional changes are documented
- OpenAPI diffs are reviewed
- smoke tests cover key success, validation, and auth paths
- generated diffs are reviewed, not just handwritten source diffs

### Phase 6 - Cleanup

Only after equivalence passes:

- remove temporary shims
- delete dead activators and legacy-only helper code
- keep the final migration artifacts and docs up to date

## What must be documented, and where

## `migration\*\plan.md`

Document here:

- why the refactor or breaking change exists
- the target end-state API or architecture
- which legacy patterns are being replaced
- what is analyzer-fixable versus manual
- compatibility-shim strategy
- rollout dependencies and sequencing
- the module-order constraints that manual app migrations should follow

## `docs\refactoring-workflow.md`

Document here:

- the layered toolchain
- the concrete single-app migration workflow
- exact commands and run modes
- expected output artifacts
- how to interpret reports
- when to use analyzers versus codemods versus normal rename/refactor flows

## `docs\implementation-checklists.md`

Document here:

- short repeatable gates for refactor work
- what must be run before merge
- what must be reviewed in generated output and reports

## `docs\compile-time-validation.md`

Document here:

- migration diagnostic ID ranges
- how migration diagnostics differ from normal generator diagnostics
- expectations for actionable messages and report output

## `README.md` and `docs\README.md`

Document here:

- pointers into the detailed docs
- commands only at a very high level

Keep the detailed workflow in `/docs`, not in the root README.

## Tool-local README files

Once the planned tool projects exist, each should have a short local README:

- `src\Blueprint.Migration.Analyzers\README.md`
- `src\Blueprint.Codemod\README.md`

Those files should explain project-specific authoring and test conventions, while this document remains the top-level workflow.

## Non-negotiables for large refactors

- Prefer Roslyn-aware rewrites over regex for structural C# changes.
- Always support a dry-run mode for large codemods.
- Emit machine-readable reports.
- Review generated diffs and snapshot diffs, not just handwritten source diffs.
- Update the relevant docs in the same change when the workflow or contract changes.


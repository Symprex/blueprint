# Copilot instructions for `blueprint-source-gen`

## Build and test commands

- `dotnet tool restore`  
  Restores local repository tools such as Husky.NET. Run this in a fresh clone before relying on the pre-commit hook.

- `dotnet husky install`  
  Installs the repository's Git hooks for the current clone. Run this once after restoring tools so staged `.cs` files are auto-formatted on commit.

- `dotnet build .\Blueprint.sln`  
  Use this for repo-wide validation. There is no separate lint command; compiler analyzers and StyleCop rules run during build, and `Directory.Build.props` enables warnings-as-errors across the repo.

- `dotnet format .\Blueprint.sln --no-restore`  
  Run this after changing C# or MSBuild files so code style is normalized before validation or commit.

- `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj`  
  Runs the main unit and snapshot tests for the generators plus runtime-model tests.

- `dotnet test .\src\Blueprint.SourceGenerator.CodeWriter.Tests\Blueprint.SourceGenerator.CodeWriter.Tests.csproj`  
  Runs the vendored `Blueprint.SourceGenerator.CodeWriter` test suite.

- `dotnet test .\src\Blueprint.SourceGenerator.Tests.Integration\Blueprint.SourceGenerator.Tests.Integration.csproj`  
  Runs the integration tests for generated pipelines and sample-style consumption.

- Single-test examples:
  - `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --filter "FullyQualifiedName~AppDataModelSourceGeneratorTests.When_not_partial_then_reports_diagnostic"`
  - `dotnet test .\src\Blueprint.SourceGenerator.CodeWriter.Tests\Blueprint.SourceGenerator.CodeWriter.Tests.csproj --filter "FullyQualifiedName~CodeWriterTests"`
  - `dotnet format .\Blueprint.sln --no-restore --include src\Blueprint.SourceGenerator\Generators\App\AppDataModelSourceGenerator.cs`

## Docs source of truth

- `/docs/README.md` is the entry point for detailed repository guidance. Treat `/docs` as the detailed source of truth for architecture, principles, observability, and AI workflow.

- If a change affects architecture, generator flow, diagnostics, observability, developer tooling, or project principles, update the relevant `/docs` file in the same change.
- If a change affects how Blueprint is consumed by downstream repos or extended in this repo, update the relevant Blueprint skill in the same change: `using-blueprint` for consumer/install guidance and `extending-blueprint` for repo-internal contributor guidance.

- Start with these docs for non-trivial work:
  - `docs/guiding-principles.md`
  - `docs/architecture.md`
  - `docs/compile-time-validation.md`
  - `docs/observability-and-dev-experience.md`
  - `docs/ai-agent-playbook.md`
  - `docs/implementation-checklists.md`

## Migration review handoff

- For migration-plan work, finish each completed slice with a self-review and then a `visual-explainer` HTML review document before commit or phase handoff.
- That review document should highlight the architectural delta, interesting code snippets, representative generated outputs or artifacts, validation evidence, review findings, open concerns, and concrete questions for the human reviewer.
- Treat human feedback from that visual review as input for another AI review/fix round before committing.

## TDD and verification expectations

- Everything in this repository follows TDD for implementation work: write the failing proof first, watch it fail for the right reason, then make the minimum production change.
- Push toward the repo-local skills when relevant:
  - `test-driven-development` before feature, bugfix, refactor, or behavior-change edits
  - `systematic-debugging` before fixing any unexpected test/build/runtime failure
  - `verification-before-completion` before claiming success, moving on, or preparing to commit
- For new planning helpers, registries, and orchestration seams, aim test coverage at happy paths, failure/diagnostic paths, and determinism edge cases.
- For pipeline or module feature work, the minimum proof set is stricter than a line-presence assertion:
  - a full generator snapshot that captures the complete pipeline shape
  - compile-time source/diagnostic tests for invalid declarative or string-backed contracts when the generator can know they are wrong
  - `Blueprint.SourceGenerator.Tests.Integration` coverage that executes the generated pipeline for both valid and invalid paths and proves short-circuiting or handler execution
- Simple generated-source substring assertions may supplement that matrix, but they do not replace snapshots, diagnostics, or runtime execution proofs.

## High-level architecture

- `Blueprint.Runtime.Model` is the contract layer. It defines CQRS operation interfaces, descriptors, links, and the `BlueprintApp` registry.

- `Blueprint.SourceGenerator` is the compile-time layer. It emits API resource helper partials, operation descriptors, app registrations, executor pipelines, and generated executors. Detailed flow lives in `docs/architecture.md`.

- `Blueprint.Runtime` is the execution and ASP.NET Core integration layer. It consumes generated metadata and generated pipelines rather than rediscovering the API surface at runtime.

- `Blueprint.SourceGenerator.Sample` and `Blueprint.SourceGenerator.Sample.Contract` are the reference consumers. They reference `Blueprint.SourceGenerator` as an analyzer (`OutputItemType="Analyzer"` and `ReferenceOutputAssembly="false"`), so they are the fastest way to see how generated code is consumed in a real app.

- `Blueprint.SourceGenerator.CodeWriter` is the vendored code-emission library used by generator code. Prefer the local CodeWriter builders over ad-hoc string concatenation when changing generator output.

## Key conventions

- Blueprint apps are `partial` classes marked with `[BlueprintAppScan]`. When operations live in another assembly, the app uses `[BlueprintAppScan(FromAssemblyOf = typeof(...))]` to bring them into the generated registration set.

- Operations are declared in the contract/model layer and implement `ICommand` or `IQuery`, both of which extend `IOperation`. The repo expects operations to expose `static ApiOperationDescriptor Configure()`, usually through generated partial code rather than manual implementations.

- Generator changes often span more than one emitter. If you change operation discovery or generated registration shape, check the descriptor generator, app registration generator, and pipeline/executor generator together so runtime registration and execution stay aligned.
- Concrete generator modules must end with the `Module` suffix and live in dedicated subfolders under `src\Blueprint.SourceGenerator\Modules` (for example `Modules\LinkGeneration\LinkGenerationModule.cs`).
- Module-specific helper files should live beside the owning module inside that same folder rather than back at the `Modules` root.
- Contributor capability methods should be named for the seam they populate (`ContributeToDescriptor`, `ContributeToPipeline`, and similar `ContributeTo<Thing>` names), not a generic `Contribute`.
- For pipeline planning, stage participation should be inferred by helper APIs such as render-step and variable registration; do not introduce or reintroduce explicit `AddContribution(...)` bookkeeping in module call sites.
- Planning context classes may expose a small read-only metadata surface when it keeps module code clearer, but they must not expose mutable plan/execution state or record-style grab-bags that bypass the approved helper surface.

- Generator tests are not just string snapshots. `src\Blueprint.SourceGenerator.Tests\TestHelpers.cs` compiles in-memory source, runs the incremental generator twice, and asserts the second run is fully cached. A change can fail tests by breaking incremental cacheability even when the generated text still looks correct.

- Snapshot baselines live under `src\Blueprint.SourceGenerator.Tests\Generators\*\Snapshots\*.verified.txt`. When intentional generator output changes occur, review the corresponding `.received.*` file and then promote it to `.verified.*`; do not update snapshots without reviewing the generated diff.

- Follow a TDD mindset for all changes. Start by identifying the relevant existing test, snapshot, or validation path; add or update that safety net first when behaviour changes; then implement and finish by rerunning the targeted tests before broader validation.
- For pipeline contributor slices, build the test matrix up front and keep it explicit:
  - snapshot test for the full generated pipeline
  - compile-time invalid-configuration test when symbol-backed validation is possible
  - runtime integration test that executes the generated pipeline for success and failure
- If one of those layers truly does not apply, record why in the session plan and the relevant migration plan before coding.
- When code needs comments, use them to explain the invariant, constraint, fallback boundary, or trade-off that is not obvious from the code alone. Do not add comments that simply restate what the next line already says.
- Prefer `static` lambdas in LINQ and similar delegate-heavy code whenever the lambda does not need to capture local state.
- Keep module-specific tests in module-specific test files. Do not keep growing shared catch-all test files with cases that only exercise one module.

- Prefer compile-time validation over runtime repair. `LinkGenerationModule` and the BP/BPLG diagnostics are the pattern to follow. When a string literal or declarative contract can be proven at build time, surface it as a diagnostic.

- This repo uses Husky.NET to run `dotnet format Blueprint.sln --no-restore --include ${staged}` on staged `.cs` files during `pre-commit` once hooks are installed, then re-stages the formatted files before the commit continues.

- Repo-wide style comes from `src\.editorconfig` and `src\Stylecop.json`: C# uses 4 spaces, `using` directives stay outside namespaces, private fields use an underscore prefix, `this.` qualification is preferred, async methods must end with `Async`, nullable is enabled, and preview C# features are allowed.

- Keep a keen eye on observability and inspection surfaces. Generated pipelines already emit activity and structured logging hooks; future runtime work should continue moving toward full OpenTelemetry and OTLP-friendly observability, plus developer- and AI-friendly diagnostics or tooling for large API surfaces.

- If you change link, URL, or marker-attribute generation, update both the module layer (`IBlueprintSourceGeneratorModule` / `LinkGenerationModule`) and the descriptor generator so analysis-time metadata and emitted descriptor code stay in sync.

- To inspect generated files in the sample consumers, reuse the commented `EmitCompilerGeneratedFiles` and `CompilerGeneratedFilesOutputPath` settings already present in the sample `.csproj` files instead of inventing a separate workflow.

- Use conventional commit messages for repository commits (for example `docs: ...`, `feat: ...`, `fix: ...`, `chore: ...`).

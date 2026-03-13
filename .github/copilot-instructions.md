# Copilot instructions for `blueprint-source-gen`

## Build and test commands

- `dotnet build .\Blueprint.sln`  
  Use this for repo-wide validation. There is no separate lint command; compiler analyzers and StyleCop rules run during build, and `Directory.Build.props` enables warnings-as-errors across the repo.

- `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj`  
  Runs the main unit and snapshot tests for the generators plus runtime-model tests.

- `dotnet test .\src\Blueprint.SourceGenerator.CodeWriter.Tests\Blueprint.SourceGenerator.CodeWriter.Tests.csproj`  
  Runs the vendored `Blueprint.SourceGenerator.CodeWriter` test suite.

- `dotnet test .\src\Blueprint.SourceGenerator.Tests.Integration\Blueprint.SourceGenerator.Tests.Integration.csproj`  
  Runs the integration tests for generated pipelines and sample-style consumption.

- Single-test examples:
  - `dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --filter "FullyQualifiedName~AppDataModelSourceGeneratorTests.When_not_partial_then_reports_diagnostic"`
  - `dotnet test .\src\Blueprint.SourceGenerator.CodeWriter.Tests\Blueprint.SourceGenerator.CodeWriter.Tests.csproj --filter "FullyQualifiedName~CodeWriterTests"`

## High-level architecture

- `Blueprint.Runtime.Model` is the contract layer. It defines the CQRS operation interfaces (`IOperation`, `ICommand`, `IQuery`), operation attributes, `ApiOperationDescriptor`, and `BlueprintApp`, which is the runtime registry for operations and resource links.

- `Blueprint.SourceGenerator` is the compile-time layer. It contains multiple incremental generators that work together:
  - `Generators\App\AppDataModelSourceGenerator.cs` scans `[BlueprintAppScan]` types and emits `*_DataModelRegistration.g.cs` partials that call `this.Register(Operation.Configure())`.
  - `Generators\Descriptors\OperationDescriptorSourceGenerator.cs` discovers `IOperation` implementations and emits `*_Descriptor.g.cs` partials that add `static Configure()` implementations.
  - `Generators\Pipelines\OperationExecutorPipelineSourceGenerator.cs` discovers handlers and emits `*ExecutorPipeline.g.cs` plus `*Executor.g.cs` for runtime dispatch.
  - Descriptor generation is extensible through `IBlueprintSourceGeneratorModule` implementations such as `LinkGenerationModule`, which register marker attributes and contribute descriptor metadata/code.

- `Blueprint.Runtime` is the execution and ASP.NET Core integration layer. `BlueprintAppExecutor` maps operation types to generated pipelines, and `MapBlueprintApi<TApp, TExec>()` turns registered operation URLs into ASP.NET Core endpoints that execute via the generated executor.

- `Blueprint.SourceGenerator.Sample` and `Blueprint.SourceGenerator.Sample.Contract` are the reference consumers. They reference `Blueprint.SourceGenerator` as an analyzer (`OutputItemType="Analyzer"` and `ReferenceOutputAssembly="false"`), so they are the fastest way to see how generated code is consumed in a real app.

- `Blueprint.SourceGenerator.CodeWriter` is the vendored code-emission library used by generator code. Prefer the local CodeWriter builders over ad-hoc string concatenation when changing generator output.

## Key conventions

- Blueprint apps are `partial` classes marked with `[BlueprintAppScan]`. When operations live in another assembly, the app uses `[BlueprintAppScan(FromAssemblyOf = typeof(...))]` to bring them into the generated registration set.

- Operations are declared in the contract/model layer and implement `ICommand` or `IQuery`, both of which extend `IOperation`. The repo expects operations to expose `static ApiOperationDescriptor Configure()`, usually through generated partial code rather than manual implementations.

- Generator changes often span more than one emitter. If you change operation discovery or generated registration shape, check the descriptor generator, app registration generator, and pipeline/executor generator together so runtime registration and execution stay aligned.

- Generator tests are not just string snapshots. `src\Blueprint.SourceGenerator.Tests\TestHelpers.cs` compiles in-memory source, runs the incremental generator twice, and asserts the second run is fully cached. A change can fail tests by breaking incremental cacheability even when the generated text still looks correct.

- Snapshot baselines live under `src\Blueprint.SourceGenerator.Tests\Generators\*\Snapshots\*.verified.txt`. When intentional generator output changes occur, review the corresponding `.received.*` file and then promote it to `.verified.*`; do not update snapshots without reviewing the generated diff.

- Follow a TDD mindset for all changes. Start by identifying the relevant existing test, snapshot, or validation path; add or update that safety net first when behaviour changes; then implement and finish by rerunning the targeted tests before broader validation.

- Repo-wide style comes from `src\.editorconfig` and `src\Stylecop.json`: C# uses 4 spaces, `using` directives stay outside namespaces, private fields use an underscore prefix, `this.` qualification is preferred, async methods must end with `Async`, nullable is enabled, and preview C# features are allowed.

- XML documentation is not globally required by StyleCop here (`documentExposedElements` is disabled). Do not add blanket XML docs just to satisfy analyzers; match the documentation level already present in the area you are editing unless the task is explicitly about docs.

- The repo vendors `Blueprint.SourceGenerator.CodeWriter` locally instead of depending on the old external CodeGenHelpers package. Prefer the local CodeWriter API and project references when changing generator output or packaging.

- If you change link, URL, or marker-attribute generation, update both the module layer (`IBlueprintSourceGeneratorModule` / `LinkGenerationModule`) and the descriptor generator so analysis-time metadata and emitted descriptor code stay in sync.

- To inspect generated files in the sample consumers, reuse the commented `EmitCompilerGeneratedFiles` and `CompilerGeneratedFilesOutputPath` settings already present in the sample `.csproj` files instead of inventing a separate workflow.

- Use conventional commit messages for repository commits (for example `docs: ...`, `feat: ...`, `fix: ...`, `chore: ...`).

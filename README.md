# Blueprint Source Generator

Blueprint Source Generator is a .NET 8 / Roslyn-based framework for building CQRS-style APIs with compile-time code generation instead of runtime reflection.

The repository combines:

- `Blueprint.Runtime.Model` for the core contracts, descriptors, attributes, and `BlueprintApp` registry
- `Blueprint.Runtime` for runtime execution and ASP.NET Core endpoint wiring
- `Blueprint.SourceGenerator` for incremental generators that emit operation descriptors, app registrations, and executor pipelines
- `Blueprint.SourceGenerator.CodeWriter` for the vendored fluent C# code-emission library used by the generators
- sample and contract projects that show how the generators are consumed from a real app

## Requirements

- .NET SDK `8.0.206` (see `global.json`)

## Build and test

```powershell
dotnet build .\Blueprint.sln
dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj
dotnet test .\src\Blueprint.SourceGenerator.CodeWriter.Tests\Blueprint.SourceGenerator.CodeWriter.Tests.csproj
dotnet test .\src\Blueprint.SourceGenerator.Tests.Integration\Blueprint.SourceGenerator.Tests.Integration.csproj
```

Run a single test with a filter, for example:

```powershell
dotnet test .\src\Blueprint.SourceGenerator.Tests\Blueprint.SourceGenerator.Tests.csproj --filter "FullyQualifiedName~AppDataModelSourceGeneratorTests.When_not_partial_then_reports_diagnostic"
```

## How the pieces fit together

Blueprint apps are declared as `partial` classes marked with `[BlueprintAppScan]`. During compilation, the source generators:

1. discover Blueprint app types and operations
2. emit `static Configure()` implementations for operations
3. emit partial app registrations that call `this.Register(Operation.Configure())`
4. emit executor pipelines and generated executors for runtime dispatch

At runtime, `BlueprintApp` holds the registered `ApiOperationDescriptor` instances, `BlueprintAppExecutor` maps operation types to pipelines, and `MapBlueprintApi<TApp, TExec>()` exposes the generated operations as ASP.NET Core endpoints.

## Development notes

- Generator changes usually touch more than one layer. Descriptor generation, app registration, and executor pipeline generation need to stay aligned.
- Snapshot baselines for generator tests live under `src\Blueprint.SourceGenerator.Tests\Generators\*\Snapshots\`.
- `TestHelpers.Compile<T>()` verifies incremental cacheability as well as generated output, so generator changes can fail tests even when snapshots still look reasonable.
- To inspect generated files in sample consumers, reuse the commented `EmitCompilerGeneratedFiles` settings already present in the sample `.csproj` files.

## AI assistant guidance

Repository-specific Copilot guidance lives in `.github/copilot-instructions.md`.

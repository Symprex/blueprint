# Blueprint Source Generator - Project Overview

## Purpose and Architecture

The Blueprint Source Generator is a **C# source generator framework** that enables building **CQRS-style APIs** with automated code generation. It uses Roslyn analyzers to scan code at compile-time and generate API operation descriptors, routing configurations, and resource link relationships.

### Core Design Principles

1. **Code Generation over Reflection** - Uses compile-time analysis instead of runtime reflection
2. **CQRS Pattern Enforcement** - Strict separation between commands (write) and queries (read)
3. **Attribute-Driven Configuration** - Declarative approach using attributes for routing, auth, links
4. **Modular Architecture** - Extensible through generator modules
5. **Type Safety** - Compile-time validation and strong typing throughout

## Repository Structure

```
src/
├── Blueprint.SourceGenerator/          # Core Roslyn source generator (netstandard2.0)
│   ├── Generators/
│   │   ├── App/                       # Main app data model generator
│   │   ├── ApiResources/              # API resource generators
│   │   └── Descriptors/               # Operation descriptor generators
│   ├── Modules/                       # Extensibility modules
│   │   ├── IBlueprintSourceGeneratorModule.cs
│   │   └── LinkGenerationModule.cs
│   └── Helpers/                       # Code generation utilities
├── Blueprint.Runtime/                  # Runtime execution framework (net8.0)
│   ├── Http/                          # ASP.NET Core integration
│   │   ├── ApplicationBuilderExtensions.cs
│   │   └── BlueprintEndpointRouteBuilder.cs
│   └── Exec/                          # Operation execution pipeline
│       ├── BlueprintAppExecutor.cs
│       └── IOperationExecutorPipeline.cs
├── Blueprint.Runtime.Model/            # Core interfaces and model (net8.0)
│   ├── Attributes/                     # Operation attributes
│   │   ├── HttpMethodAttribute.cs
│   │   ├── LinkAttribute.cs
│   │   └── MustBeAuthenticatedAttribute.cs
│   ├── BlueprintApp.cs               # Central operation registry
│   ├── IOperation.cs                 # Base operation interface
│   ├── ICommand.cs / IQuery.cs       # CQRS interfaces
│   └── ApiResource.cs                # Base API resource class
├── Blueprint.SourceGenerator.Sample/   # Example ASP.NET Core app
├── Blueprint.SourceGenerator.Sample.Contract/  # Sample API contracts
├── Blueprint.SourceGenerator.Tests/              # Unit tests
└── Blueprint.SourceGenerator.Tests.Integration/  # Integration tests
```

## Key Components Deep Dive

### 1. Source Generator Pipeline

**Entry Point:** `AppDataModelSourceGenerator.cs`
- Implements `IIncrementalGenerator` for performance
- Three-stage pipeline:
  1. **InitialExtraction** - Finds classes with `[BlueprintAppScan]`
  2. **CombineWithCompilation** - Merges with compilation context
  3. **FindServicesToRegister** - Discovers operations to register

```csharp
[Generator(LanguageNames.CSharp)]
public sealed partial class AppDataModelSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Blueprint.Model.BlueprintAppScanAttribute", /*...*/);
        // Pipeline stages...
    }
}
```

### 2. Operation Model System

**Core Interfaces:**
```csharp
// Base operation marker
public interface IOperation
{
    static abstract ApiOperationDescriptor Configure();
}

// CQRS separation
public interface ICommand : IOperation { }           // Write operations
public interface IQuery : IOperation { }            // Read operations
public interface ICommand<TResponse> : ICommand, IReturn<TResponse> { }
public interface IQuery<TResponse> : IQuery, IReturn<TResponse> { }
```

### 3. Attribute System

**Operation Configuration:**
- `[BlueprintAppScan]` - Marks classes for scanning
- `[HttpGet]`, `[HttpPost]`, etc. - HTTP method specification
- `[SelfLink(typeof(ResourceType), "/route")]` - Resource self-links
- `[LinkAttribute]` - General resource-to-operation links
- `[MustBeAuthenticated]` - Authentication requirements

### 4. Runtime Architecture

**BlueprintApp** - Central registry that:
- Stores all registered operations and their descriptors
- Manages links between resources and operations
- Provides lookup methods for operations and links
- Validates uniqueness of routes and resource relationships

**Operation Execution:**
- `BlueprintAppExecutor` - Runtime pipeline coordinator
- `ApiOperationContext` - Execution context for operations
- `OperationResult` - Standardized operation responses

## Configuration Files

### Build Configuration
- `Directory.Build.props` - MSBuild properties (nullable enabled, C# preview, StyleCop)
- `global.json` - .NET SDK 8.0.206 with feature roll-forward
- `Stylecop.json` - Code style rules (documentation disabled for internal elements)
- `.editorconfig` - Formatting rules (4-space indent, underscore private fields)

### Quality Gates
- **Nullable Reference Types:** Enabled project-wide
- **Warnings as Errors:** Enabled (except vulnerability warnings)
- **Language Version:** C# preview features
- **StyleCop Analysis:** Enforced with custom rules

## Technology Stack

### Core Framework
- **.NET 8.0** for runtime components
- **netstandard2.0** for source generator (Roslyn requirement)
- **ASP.NET Core** for web hosting and HTTP integration
- **Roslyn Analyzers** for compile-time code analysis

### Key Dependencies
- **AvantiPoint.CodeGenHelpers** - Code generation utilities
- **Microsoft.CodeAnalysis.CSharp** - Roslyn C# analysis
- **Newtonsoft.Json** - JSON serialization
- **JetBrains.Annotations** - Code analysis attributes

### Testing Stack
- **xUnit** - Primary testing framework
- **FluentAssertions** - Assertion library
- **Verify.SourceGenerators** - Snapshot testing for generated code
- **NSubstitute** - Mocking framework

## Generated Code Patterns

The source generator produces code following these patterns:

### Operation Registration
```csharp
public partial class SampleBlueprintApp : BlueprintApp
{
    public SampleBlueprintApp()
    {
        this.Register(GetTemplatesQuery.Configure());
        this.Register(CreateTemplateCommand.Configure());
    }
}
```

### Operation Descriptors
```csharp
public static ApiOperationDescriptor Configure()
{
    return new ApiOperationDescriptor(typeof(GetTemplatesQuery))
    {
        Name = "GetTemplates",
        Urls = { new ApiOperationUrl("/templates", "GET") },
        Links = { /* generated links */ }
    };
}
```

## Extension Points

### Custom Modules
Implement `IBlueprintSourceGeneratorModule` to:
1. **RegisterMarkerAttributes** - Add new attributes
2. **ConfigureModel** - Inspect and modify operation models
3. **ConfigureDescriptor** - Generate custom descriptor code

### Runtime Extensions
- Implement custom `IOperationExecutorPipeline` for execution logic
- Extend `BlueprintAppExecutor` for application-specific behavior
- Create custom `OperationResult` types for different response patterns
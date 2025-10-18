# Blueprint Source Generator - Copilot Instructions

This directory contains instructions and guidelines for AI agents (like GitHub Copilot) working on the Blueprint Source Generator project. These files provide context about the codebase architecture, patterns, and conventions to ensure consistent and high-quality code generation and modifications.

## Files Overview

- **`project-overview.md`** - Comprehensive technical overview of the project architecture
- **`development-patterns.md`** - Coding patterns, conventions, and best practices
- **`testing-guidelines.md`** - Testing frameworks, patterns, and snapshot testing
- **`source-generator-guide.md`** - Specific guidance for working with Roslyn source generators
- **`api-patterns.md`** - CQRS patterns, operation definitions, and API resource creation

## Quick Reference

### Project Type
C# source generator framework for CQRS-style APIs using Roslyn analyzers.

### Key Technologies
- .NET 8.0 / netstandard2.0
- Roslyn Source Generators
- ASP.NET Core
- xUnit + Verify.SourceGenerators for testing

### Architecture Pattern
- **CQRS**: Commands (write) and Queries (read) separation
- **Attribute-driven**: Declarative configuration via attributes
- **Code generation**: Compile-time generation over runtime reflection
- **Modular**: Extensible through generator modules

### Entry Points
- Source Generator: `src/Blueprint.SourceGenerator/Generators/App/AppDataModelSourceGenerator.cs`
- Sample App: `src/Blueprint.SourceGenerator.Sample/Program.cs`
- Core Model: `src/Blueprint.Runtime.Model/BlueprintApp.cs`

## Usage

When working on this project, refer to these instruction files to understand:
1. How to create new operations (commands/queries)
2. How to extend the source generator
3. How to write tests and validate generated code
4. What patterns and conventions to follow
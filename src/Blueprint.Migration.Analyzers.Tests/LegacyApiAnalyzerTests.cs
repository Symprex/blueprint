using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Blueprint.Migration.Analyzers.Tests;

/// <summary>
/// Tests for <see cref="Analyzers.LegacyApiAnalyzer"/> which flags legacy Blueprint
/// API patterns (IMiddlewareBuilder, BlueprintApiBuilder) that have been replaced.
/// </summary>
public sealed class LegacyApiAnalyzerTests
{
    [Xunit.Fact]
    public async Task When_class_implements_IMiddlewareBuilder_then_reports_bpmig301()
    {
        const string source = """
            namespace Blueprint.Middleware
            {
                public interface IMiddlewareBuilder { }
            }

            namespace TestConsumer
            {
                public class MyMiddleware : Blueprint.Middleware.IMiddlewareBuilder { }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Should().ContainSingle(static d => d.Id == "BPMIG301");
        diagnostics.Single(static d => d.Id == "BPMIG301")
            .GetMessage().Should().Contain("MyMiddleware");
    }

    [Xunit.Fact]
    public async Task When_class_does_not_implement_IMiddlewareBuilder_then_no_bpmig301()
    {
        const string source = """
            namespace TestConsumer
            {
                public interface IMyInterface { }
                public class MyClass : IMyInterface { }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Where(static d => d.Id == "BPMIG301").Should().BeEmpty();
    }

    [Xunit.Fact]
    public async Task When_parameter_uses_BlueprintApiBuilder_then_reports_bpmig302()
    {
        const string source = """
            namespace Blueprint.Configuration
            {
                public class BlueprintApiBuilder { }
            }

            namespace TestConsumer
            {
                using Blueprint.Configuration;

                public static class Extensions
                {
                    public static BlueprintApiBuilder AddMyStuff(BlueprintApiBuilder builder) => builder;
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Where(static d => d.Id == "BPMIG302").Should().NotBeEmpty();
    }

    [Xunit.Fact]
    public async Task When_field_uses_BlueprintApiBuilder_then_reports_bpmig302()
    {
        const string source = """
            namespace Blueprint.Configuration
            {
                public class BlueprintApiBuilder { }
            }

            namespace TestConsumer
            {
                using Blueprint.Configuration;

                public class MyConfig
                {
                    private readonly BlueprintApiBuilder _builder;
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Where(static d => d.Id == "BPMIG302").Should().NotBeEmpty();
    }

    [Xunit.Fact]
    public async Task When_no_BlueprintApiBuilder_reference_then_no_bpmig302()
    {
        const string source = """
            namespace TestConsumer
            {
                public class MyConfig
                {
                    private readonly string _name = "hello";
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Where(static d => d.Id == "BPMIG302").Should().BeEmpty();
    }

    [Xunit.Fact]
    public async Task When_BlueprintApiBuilder_in_using_directive_then_no_bpmig302()
    {
        // The namespace analyzer handles using directives, not this analyzer
        const string source = """
            using Blueprint.Configuration;

            namespace TestConsumer
            {
                public class Foo { }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Where(static d => d.Id == "BPMIG302").Should().BeEmpty();
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            assemblyName: "LegacyApiMigrationTests",
            syntaxTrees: [syntaxTree],
            references: AppDomain.CurrentDomain.GetAssemblies()
                .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location)),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new Analyzers.LegacyApiAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}

using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Blueprint.Migration.Analyzers.Tests;

/// <summary>
/// Tests for <see cref="Analyzers.LegacyNamespaceAnalyzer"/> which flags legacy Blueprint
/// namespace using directives and suggests replacements.
/// </summary>
public sealed class LegacyNamespaceAnalyzerTests
{
    [Xunit.Theory]
    [Xunit.InlineData("Blueprint.Http", "Blueprint.Model, Blueprint.Runtime.Http")]
    [Xunit.InlineData("Blueprint.Http.Formatters", "Blueprint.Runtime.Http")]
    [Xunit.InlineData("Blueprint.Http.Infrastructure", "Blueprint.Runtime.Http")]
    [Xunit.InlineData("Blueprint.Http.MessagePopulation", "Blueprint.Runtime.Http")]
    [Xunit.InlineData("Blueprint.Http.Middleware", "Blueprint.Runtime.Middleware")]
    [Xunit.InlineData("Blueprint.Authorisation", "Blueprint.Authorization")]
    [Xunit.InlineData("Blueprint.Errors", "Blueprint")]
    [Xunit.InlineData("Blueprint.ThirdParty", "Blueprint.Utilities")]
    [Xunit.InlineData("Blueprint.Configuration", "Microsoft.Extensions.DependencyInjection")]
    [Xunit.InlineData("Blueprint.Properties", "Blueprint.Model")]
    public async Task When_legacy_namespace_using_exists_then_reports_bpmig300(string legacyNamespace, string expectedReplacement)
    {
        var source =
            "using " + legacyNamespace + ";\n" +
            "namespace TestConsumer { public class Foo { } }";

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Should().ContainSingle(static d => d.Id == "BPMIG300");
        var diagnostic = diagnostics.Single(static d => d.Id == "BPMIG300");
        diagnostic.GetMessage().Should().Contain(legacyNamespace);
        diagnostic.GetMessage().Should().Contain(expectedReplacement);
    }

    [Xunit.Theory]
    [Xunit.InlineData("Blueprint.Model")]
    [Xunit.InlineData("Blueprint.Authorization")]
    [Xunit.InlineData("Blueprint.Utilities")]
    [Xunit.InlineData("Blueprint.Validation")]
    [Xunit.InlineData("Blueprint")]
    [Xunit.InlineData("Blueprint.Runtime.Http")]
    [Xunit.InlineData("Blueprint.Runtime.Middleware")]
    [Xunit.InlineData("Blueprint.Runtime.Tasks")]
    [Xunit.InlineData("Blueprint.Tasks")]
    [Xunit.InlineData("Blueprint.Diagnostics")]
    [Xunit.InlineData("Blueprint.Middleware")]
    [Xunit.InlineData("Microsoft.Extensions.DependencyInjection")]
    [Xunit.InlineData("System.ComponentModel.DataAnnotations")]
    public async Task When_current_namespace_using_exists_then_no_diagnostic(string currentNamespace)
    {
        var source =
            "using " + currentNamespace + ";\n" +
            "namespace TestConsumer { public class Foo { } }";

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Where(static d => d.Id == "BPMIG300").Should().BeEmpty();
    }

    [Xunit.Fact]
    public async Task When_static_using_with_legacy_namespace_then_no_diagnostic()
    {
        const string source = """
            using static Blueprint.Http.SomeClass;

            namespace TestConsumer
            {
                public class Foo { }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Where(static d => d.Id == "BPMIG300").Should().BeEmpty();
    }

    [Xunit.Fact]
    public async Task When_alias_using_with_legacy_namespace_then_no_diagnostic()
    {
        const string source = """
            using Http = Blueprint.Http;

            namespace TestConsumer
            {
                public class Foo { }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Where(static d => d.Id == "BPMIG300").Should().BeEmpty();
    }

    [Xunit.Fact]
    public async Task When_multiple_legacy_namespaces_then_reports_multiple_diagnostics()
    {
        const string source = """
            using Blueprint.Http;
            using Blueprint.Authorisation;
            using Blueprint.Errors;

            namespace TestConsumer
            {
                public class Foo { }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Where(static d => d.Id == "BPMIG300").Should().HaveCount(3);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            assemblyName: "LegacyNamespaceMigrationTests",
            syntaxTrees: [syntaxTree],
            references: AppDomain.CurrentDomain.GetAssemblies()
                .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location)),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new Analyzers.LegacyNamespaceAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}

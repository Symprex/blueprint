using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Blueprint.Migration.Analyzers.Tests;

public sealed class LegacyActivationAnalyzerTests
{
    [Xunit.Fact]
    public async Task When_legacy_blueprint_builder_calls_addopenapi_then_reports_bpmig001_on_the_invocation()
    {
        const string source = """
            namespace Legacy.Blueprint;

            public sealed class BlueprintApiBuilder
            {
            }

            public sealed class OpenApiOptions
            {
            }

            public static class BlueprintApiBuilderExtensions
            {
                public static BlueprintApiBuilder AddOpenApi(this BlueprintApiBuilder pipelineBuilder)
                    => pipelineBuilder;
            }

            public static class ConsumerConfiguration
            {
                public static void Configure(BlueprintApiBuilder builder)
                {
                    builder.AddOpenApi();
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Should().ContainSingle(static diagnostic => diagnostic.Id == "BPMIG001");

        var diagnostic = diagnostics[0];
        diagnostic.GetMessage().Should().Contain("AddOpenApi");
        diagnostic.Location.GetLineSpan().StartLinePosition.Line.Should().Be(20);
    }

    [Xunit.Fact]
    public async Task When_legacy_blueprint_builder_configures_addopenapi_then_does_not_report_bpmig001()
    {
        const string source = """
            namespace Legacy.Blueprint;

            using System;

            public sealed class BlueprintApiBuilder
            {
            }

            public sealed class OpenApiOptions
            {
                public string? Title { get; set; }
            }

            public static class BlueprintApiBuilderExtensions
            {
                public static BlueprintApiBuilder AddOpenApi(
                    this BlueprintApiBuilder pipelineBuilder,
                    Action<OpenApiOptions> configure)
                    => pipelineBuilder;
            }

            public static class ConsumerConfiguration
            {
                public static void Configure(BlueprintApiBuilder builder)
                {
                    builder.AddOpenApi(options => options.Title = "Legacy");
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Should().NotContain(static diagnostic => diagnostic.Id == "BPMIG001");
    }

    [Xunit.Fact]
    public async Task When_legacy_blueprint_builder_configures_addopenapi_then_reports_bpmig100_with_configureopenapi_guidance()
    {
        const string source = """
            namespace Legacy.Blueprint;

            using System;

            public sealed class BlueprintApiBuilder
            {
            }

            public sealed class OpenApiOptions
            {
                public string? Title { get; set; }
            }

            public static class BlueprintApiBuilderExtensions
            {
                public static BlueprintApiBuilder AddOpenApi(
                    this BlueprintApiBuilder pipelineBuilder,
                    Action<OpenApiOptions> configure)
                    => pipelineBuilder;
            }

            public static class ConsumerConfiguration
            {
                public static void Configure(BlueprintApiBuilder builder)
                {
                    builder.AddOpenApi(options => options.Title = "Legacy");
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Should().ContainSingle(static diagnostic => diagnostic.Id == "BPMIG100");

        var diagnostic = diagnostics.Single(static candidate => candidate.Id == "BPMIG100");
        diagnostic.GetMessage().Should().Contain("ConfigureOpenApi");
        diagnostic.GetMessage().Should().Contain("AddOpenApi");
    }

    [Xunit.Fact]
    public async Task When_legacy_blueprint_builder_calls_addhateoaslinks_then_reports_bpmig002_on_the_invocation()
    {
        const string source = """
            namespace Legacy.Blueprint;

            public sealed class BlueprintApiBuilder
            {
            }

            public static class BlueprintApiBuilderExtensions
            {
                public static BlueprintApiBuilder AddHateoasLinks(this BlueprintApiBuilder apiBuilder)
                    => apiBuilder;
            }

            public static class ConsumerConfiguration
            {
                public static void Configure(BlueprintApiBuilder builder)
                {
                    builder.AddHateoasLinks();
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Should().ContainSingle(static diagnostic => diagnostic.Id == "BPMIG002");

        var diagnostic = diagnostics.Single(static candidate => candidate.Id == "BPMIG002");
        diagnostic.GetMessage().Should().Contain("AddHateoasLinks");
        diagnostic.Location.GetLineSpan().StartLinePosition.Line.Should().Be(16);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            assemblyName: "LegacyBlueprintMigrationTests",
            syntaxTrees: [syntaxTree],
            references: AppDomain.CurrentDomain.GetAssemblies()
                .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location)),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzers = typeof(Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors).Assembly
            .GetTypes()
            .Where(static type => !type.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
            .Select(static type => (DiagnosticAnalyzer)Activator.CreateInstance(type)!)
            .ToImmutableArray();

        if (analyzers.IsDefaultOrEmpty)
        {
            return ImmutableArray<Diagnostic>.Empty;
        }

        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}

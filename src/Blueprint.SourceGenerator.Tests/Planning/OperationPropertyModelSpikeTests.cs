using System.Collections.Immutable;
using Blueprint.SourceGenerator.Generators.App;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Planning;

/// <summary>
/// Spike for Symprex/blueprint#75 (D25): an operation declared only as <c>[Expose&lt;Inner.X&gt;] partial class X</c>
/// gets its properties from one shared operation property model, which every generator builds from its own compilation,
/// and which the descriptor generator also emits into the partial.
/// </summary>
public class OperationPropertyModelSpikeTests
{
    // The attribute #65 adds to Blueprint.Model; declared in source here so the spike adds no public API.
    private const string ExposeAttributeSource = """
        namespace Blueprint.Model
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class ExposeAttribute<TInner> : System.Attribute
                where TInner : IOperation
            {
            }
        }
        """;

    private const string InnerSource = """
        namespace Inner
        {
            using System.ComponentModel.DataAnnotations;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            public class WidgetResult
            {
                public string Id { get; set; } = "";
            }

            [RootLink("/widgets/{Id}")]
            public partial class GetWidgetQuery : IQuery<WidgetResult>
            {
                /// <summary>The widget to get.</summary>
                [Required]
                public string Id { get; set; } = "";

                /// <summary>How many revisions to include.</summary>
                [Range(1, 10)]
                public int Revisions { get; set; } = 1;

                /// <summary>The region to read from.</summary>
                [FromHeader("X-Region")]
                public string? Region { get; set; }

                public string ComputedOnly => this.Id + this.Region;

                public static WidgetResult Handle(GetWidgetQuery query) => new() { Id = query.Id };
            }
        }
        """;

    private const string GatewaySource = """
        namespace Gateway
        {
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            [Expose<Inner.GetWidgetQuery>]
            [AllowAnonymous]
            [RootLink("/gateway/widgets/{Id}")]
            public partial class GetGatewayWidgetQuery : IQuery<Inner.WidgetResult>;

            public partial class GetGatewayWidgetQuery
            {
                public static Inner.WidgetResult Handle(GetGatewayWidgetQuery query) => new() { Id = query.Id + query.Revisions + query.Region };
            }
        }
        """;

    private const string AppSource = """
        namespace Gateway
        {
            using Blueprint.Model;

            [BlueprintAppScan]
            public partial class GatewayApp : BlueprintApp;
        }
        """;

    [Fact]
    public void The_model_of_an_exposed_operation_is_its_inner_operations_public_settable_properties()
    {
        var compilation = CreateCompilation(ExposeAttributeSource, InnerSource, GatewaySource);
        var gateway = compilation.GetTypeByMetadataName("Gateway.GetGatewayWidgetQuery")!;

        var model = OperationPropertyModelBuilder.Build(gateway);

        model.ExposedTypeName.Should().Be("Inner.GetWidgetQuery");
        model.Properties.Select(static p => (p.Name, p.TypeName, p.Origin)).Should().Equal(
            ("Id", "string", OperationPropertyOrigin.Contributed),
            ("Revisions", "int", OperationPropertyOrigin.Contributed),
            ("Region", "string?", OperationPropertyOrigin.Contributed));
        model.Properties[0].Attributes.Should().Equal("global::System.ComponentModel.DataAnnotations.RequiredAttribute");
        model.Properties[1].Attributes.Should().Equal("global::System.ComponentModel.DataAnnotations.RangeAttribute(1, 10)");
        model.Properties[2].Attributes.Should().Equal("global::Blueprint.Model.Attributes.FromHeaderAttribute(\"X-Region\")");
        model.Properties[0].DocumentationCommentXml.Should().Contain("The widget to get.");
        model.Properties.Should().OnlyContain(static p => p.Location != null && p.Location.FilePath == "Gateway.cs");
    }

    [Fact]
    public void The_model_is_equal_when_built_again_from_a_new_compilation_of_the_same_source()
    {
        var first = OperationPropertyModelBuilder.Build(
            CreateCompilation(ExposeAttributeSource, InnerSource, GatewaySource).GetTypeByMetadataName("Gateway.GetGatewayWidgetQuery")!);
        var second = OperationPropertyModelBuilder.Build(
            CreateCompilation(ExposeAttributeSource, InnerSource, GatewaySource).GetTypeByMetadataName("Gateway.GetGatewayWidgetQuery")!);

        second.Should().Be(first);
        second.GetHashCode().Should().Be(first.GetHashCode());
    }

    [Fact]
    public void A_property_the_exposing_operation_declares_is_not_contributed()
    {
        var gatewaySource = GatewaySource.Replace(
            "public static Inner.WidgetResult Handle",
            "public int Revisions { get; set; } = 3;\n        public static Inner.WidgetResult Handle",
            StringComparison.Ordinal);
        var gateway = CreateCompilation(ExposeAttributeSource, InnerSource, gatewaySource).GetTypeByMetadataName("Gateway.GetGatewayWidgetQuery")!;

        var model = OperationPropertyModelBuilder.Build(gateway);

        model.Properties.Select(static p => (p.Name, p.Origin)).Should().Equal(
            ("Revisions", OperationPropertyOrigin.Declared),
            ("Id", OperationPropertyOrigin.Contributed),
            ("Region", OperationPropertyOrigin.Contributed));
    }

    [Fact]
    public void The_descriptor_generator_emits_the_contributed_properties_and_binds_and_documents_them_with_cacheable_output()
    {
        // Compile<T> emits the compilation with the generated sources, so the binding the descriptor names must compile
        // against the emitted properties, and asserts the second run is fully cached.
        var result = TestHelpers.Compile<OperationDescriptorSourceGenerator>(
            string.Join("\n", ExposeAttributeSource, InnerSource, GatewaySource));

        result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        var emitted = Generated(result.Output, "Gateway.GetGatewayWidgetQuery_ExposedProperties.g.cs");
        emitted.Should().Contain("[global::System.ComponentModel.DataAnnotations.RequiredAttribute]")
            .And.Contain("public string Id { get; set; } = default!;")
            .And.Contain("[global::System.ComponentModel.DataAnnotations.RangeAttribute(1, 10)]")
            .And.Contain("public int Revisions { get; set; }")
            .And.Contain("[global::Blueprint.Model.Attributes.FromHeaderAttribute(\"X-Region\")]")
            .And.Contain("public string? Region { get; set; }")
            .And.Contain("/// <inheritdoc cref=\"Inner.GetWidgetQuery.Id\"/>")
            .And.NotContain("ComputedOnly");

        var descriptor = Generated(result.Output, "Gateway.GetGatewayWidgetQuery_Descriptor.g.cs");

        // Property ownership: route, query and header, exactly as the inner operation's would be.
        descriptor.Should().Contain("new global::Blueprint.Model.OperationPropertyBindingEntry(\"Id\", global::Blueprint.Model.PropertyBindingSource.Route")
            .And.Contain("new global::Blueprint.Model.OperationPropertyBindingEntry(\"Revisions\", global::Blueprint.Model.PropertyBindingSource.Query")
            .And.Contain("new global::Blueprint.Model.OperationPropertyBindingEntry(\"Region\", global::Blueprint.Model.PropertyBindingSource.Header");

        // OpenAPI documentation: the inner property summaries.
        descriptor.Should().Contain("new(\"Id\", \"The widget to get.\")")
            .And.Contain("new(\"Revisions\", \"How many revisions to include.\")");

        // BPPO012: {Id} resolves to the contributed property.
        result.Diagnostics.Should().NotContain(static d => d.Id == "BPPO012");
    }

    [Fact]
    public void The_descriptor_and_emitted_properties_follow_an_edit_to_the_inner_operation_in_another_file()
    {
        // In an IDE the driver is reused across edits. An edit to the inner operation leaves the exposing operation's own
        // file untouched, so any step keyed on that file alone would keep a stale descriptor.
        var updatedInner = InnerSource.Replace(
            "public string ComputedOnly",
            "/// <summary>The colour.</summary>\n        public string? Colour { get; set; }\n\n        public string ComputedOnly",
            StringComparison.Ordinal);

        var result = TestHelpers.CompileWithTrackedGeneratorsAfterEdit(
            [TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>()],
            [ExposeAttributeSource, InnerSource, GatewaySource],
            [ExposeAttributeSource, updatedInner, GatewaySource],
            ["Expose.cs", "Inner.cs", "Gateway.cs"]);

        result.Updated.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        result.GetChangedGeneratedSourceHintNames(nameof(OperationDescriptorSourceGenerator)).Should().Contain(
        [
            "Gateway.GetGatewayWidgetQuery_Descriptor.g.cs",
            "Gateway.GetGatewayWidgetQuery_ExposedProperties.g.cs",
        ]);
        Updated(result, "Gateway.GetGatewayWidgetQuery_Descriptor.g.cs").Should().Contain("OperationPropertyBindingEntry(\"Colour\"");
        Updated(result, "Gateway.GetGatewayWidgetQuery_ExposedProperties.g.cs").Should().Contain("public string? Colour { get; set; }");
    }

    [Fact]
    public void Every_generator_together_compiles_the_exposed_operation_and_a_second_run_is_fully_cached()
    {
        // The pipeline generator binds the contributed properties without seeing the descriptor generator's partial, so
        // its output compiles only because both read the same model.
        var sources = ImmutableArray.Create(ExposeAttributeSource, InnerSource, GatewaySource, AppSource);
        var generators = new IIncrementalGenerator[]
        {
            new OperationDescriptorSourceGenerator(),
            new OperationExecutorPipelineSourceGenerator(),
            new AppDataModelSourceGenerator(),
        };
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compilation = CreateCompilation([.. sources]);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators.Select(static g => g.AsSourceGenerator()),
            parseOptions: parseOptions,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        var first = driver.GetRunResult();
        first.Results.Should().OnlyContain(static r => r.Exception == null);

        var emit = output.Emit(new MemoryStream());
        emit.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        first.GeneratedTrees.Select(static t => Path.GetFileName(t.FilePath)).Should().Contain(
        [
            "Gateway.GetGatewayWidgetQuery_ExposedProperties.g.cs",
            "Gateway.GetGatewayWidgetQueryExecutorPipeline.g.cs",
        ]);

        var pipeline = first.GeneratedTrees.Single(static t => t.FilePath.EndsWith("Gateway.GetGatewayWidgetQueryExecutorPipeline.g.cs", StringComparison.Ordinal)).ToString();
        pipeline.Should().Contain(".Revisions =").And.Contain(".Region =");

        // The descriptor and app generators replay from cache. The pipeline generator re-renders on every compilation, as
        // it always has (its tests opt out with assertCacheability: false), so it is held to identical output instead.
        var second = driver.RunGenerators(compilation.Clone()).GetRunResult();
        foreach (var index in new[] { 0, 2 })
        {
            second.Results[index].TrackedOutputSteps.SelectMany(static s => s.Value).SelectMany(static s => s.Outputs)
                .Should().OnlyContain(
                    static o => o.Reason == IncrementalStepRunReason.Cached || o.Reason == IncrementalStepRunReason.Unchanged,
                    generators[index].GetType().Name + " should replay from cache");
        }

        second.Results[1].GeneratedSources.Select(static s => s.SourceText.ToString())
            .Should().Equal(first.Results[1].GeneratedSources.Select(static s => s.SourceText.ToString()));
    }

    [Fact]
    public void A_required_inner_property_is_emitted_required_and_the_descriptor_creates_the_operation_without_setting_it()
    {
        var result = TestHelpers.Compile<OperationDescriptorSourceGenerator>(
            string.Join("\n", ExposeAttributeSource, InnerSource.Replace("public string Id { get; set; } = \"\";", "public required string Id { get; set; }", StringComparison.Ordinal), GatewaySource));

        result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        Generated(result.Output, "Gateway.GetGatewayWidgetQuery_ExposedProperties.g.cs").Should().Contain("public required string Id { get; set; }");
        Generated(result.Output, "Gateway.GetGatewayWidgetQuery_Descriptor.g.cs").Should().Contain("System.Activator.CreateInstance(typeof(GetGatewayWidgetQuery))");
    }

    private static string Generated(IEnumerable<SyntaxTree> output, string hintName)
    {
        return output.Single(t => t.FilePath.EndsWith(hintName, StringComparison.Ordinal)).ToString();
    }

    private static string Updated(MultiGeneratorEditResult result, string hintName)
    {
        return result.Updated.GetGeneratedSources(nameof(OperationDescriptorSourceGenerator))
            .Single(s => s.HintName == hintName).SourceText;
    }

    private static CSharpCompilation CreateCompilation(params string[] sources)
    {
        string[] paths = ["Expose.cs", "Inner.cs", "Gateway.cs", "App.cs"];
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Diagnose);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Where(static a => a.GetName().Name is { } name
                && !name.Contains("Marten", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("Rebus", StringComparison.OrdinalIgnoreCase))
            .Select(static a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .Append(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(Blueprint.Runtime.Exec.ApiOperationContext).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(System.ComponentModel.DataAnnotations.ValidationResult).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Http.HttpContext).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Http.IQueryCollection).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Primitives.StringValues).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Logging.ILogger).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Logging.LoggerMessage).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Options.IOptions<>).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(System.Diagnostics.Activity).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(System.Text.Json.Serialization.JsonSerializerContext).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(Blueprint.OperationResult).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(HttpRequestMessage).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(Blueprint.HttpClient.BlueprintHttpClient<>).Assembly.Location));

        return CSharpCompilation.Create(
            "OperationPropertyModelSpike",
            sources.Select((s, i) => CSharpSyntaxTree.ParseText(s, parseOptions, paths[i])),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }
}

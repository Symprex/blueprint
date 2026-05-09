using System.Text;
using Blueprint.SourceGenerator.Generators.Descriptors;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules;

public class OperationDocumentationModuleTests
{
    private const string DescriptorSnapshotsFolder = "..\\Generators\\OperationDescriptor\\Snapshots\\";

    [Fact]
    public void When_operation_has_xml_summary_then_descriptor_emits_documentation_feature_data()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                /// <summary>
                /// Retrieves a widget by its unique identifier.
                /// </summary>
                [RootLink("/widgets/{Id}")]
                public partial class GetWidgetQuery : IQuery<string>
                {
                    public string Id { get; set; }

                    public static Task<string> HandleAsync(GetWidgetQuery op) => Task.FromResult("ok");
                }
            }
            """;

        var result = TestHelpers.Compile<OperationDescriptorSourceGenerator>(source);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output.Select(static t => t.ToString())
            .Single(static s => s.Contains("GetWidgetQueryDescriptor"));

        descriptorSource.Should().Contain("OperationDocumentationFeatureData");
        descriptorSource.Should().Contain("Retrieves a widget by its unique identifier.");
    }

    [Fact]
    public void When_operation_has_no_xml_documentation_then_descriptor_does_not_emit_feature_data()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [RootLink("/items/{Id}")]
                public partial class GetItemQuery : IQuery<string>
                {
                    public string Id { get; set; }

                    public static Task<string> HandleAsync(GetItemQuery op) => Task.FromResult("ok");
                }
            }
            """;

        var result = TestHelpers.Compile<OperationDescriptorSourceGenerator>(source);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output.Select(static t => t.ToString())
            .Single(static s => s.Contains("GetItemQueryDescriptor"));

        descriptorSource.Should().NotContain("OperationDocumentationFeatureData");
    }

    [Fact]
    public void When_operation_has_xml_remarks_then_descriptor_emits_remarks_in_feature_data()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                /// <summary>
                /// Creates a new widget.
                /// </summary>
                /// <remarks>
                /// The widget will be assigned a unique identifier on creation.
                /// </remarks>
                [RootLink("/widgets")]
                public partial class CreateWidgetCommand : ICommand<string>
                {
                    public string Name { get; set; }

                    public static Task<string> HandleAsync(CreateWidgetCommand op) => Task.FromResult("ok");
                }
            }
            """;

        var result = TestHelpers.Compile<OperationDescriptorSourceGenerator>(source);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output.Select(static t => t.ToString())
            .Single(static s => s.Contains("CreateWidgetCommandDescriptor"));

        descriptorSource.Should().Contain("Creates a new widget.");
        descriptorSource.Should().Contain("The widget will be assigned a unique identifier on creation.");
    }

    [Fact]
    public void When_operation_properties_have_xml_summaries_then_descriptor_emits_property_summaries()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                /// <summary>
                /// Updates an existing widget.
                /// </summary>
                [RootLink("/widgets/{Id}")]
                public partial class UpdateWidgetCommand : ICommand<string>
                {
                    /// <summary>
                    /// The unique identifier of the widget to update.
                    /// </summary>
                    public string Id { get; set; }

                    /// <summary>
                    /// The new display name for the widget.
                    /// </summary>
                    public string Name { get; set; }

                    public static Task<string> HandleAsync(UpdateWidgetCommand op) => Task.FromResult("ok");
                }
            }
            """;

        var result = TestHelpers.Compile<OperationDescriptorSourceGenerator>(source);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output.Select(static t => t.ToString())
            .Single(static s => s.Contains("UpdateWidgetCommandDescriptor"));

        descriptorSource.Should().Contain("The unique identifier of the widget to update.");
        descriptorSource.Should().Contain("The new display name for the widget.");
        descriptorSource.Should().Contain("\"Id\"");
        descriptorSource.Should().Contain("\"Name\"");
    }

    [Fact]
    public void When_xml_doc_contains_see_cref_tags_then_tags_are_stripped_from_output()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                /// <summary>
                /// Returns the <see cref="WidgetResource"/> identified by Id.
                /// </summary>
                [RootLink("/widgets/{Id}")]
                public partial class GetWidgetByIdQuery : IQuery<string>
                {
                    public string Id { get; set; }

                    public static Task<string> HandleAsync(GetWidgetByIdQuery op) => Task.FromResult("ok");
                }
            }
            """;

        var result = TestHelpers.Compile<OperationDescriptorSourceGenerator>(source);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output.Select(static t => t.ToString())
            .Single(static s => s.Contains("GetWidgetByIdQueryDescriptor"));

        descriptorSource.Should().Contain("Returns the identified by Id.");
        descriptorSource.Should().NotContain("<see");
        descriptorSource.Should().NotContain("cref=");
    }

    [Fact]
    public void When_only_properties_have_xml_docs_then_descriptor_still_emits_feature_data_with_null_summary()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [RootLink("/things/{Id}")]
                public partial class GetThingQuery : IQuery<string>
                {
                    /// <summary>
                    /// The thing identifier.
                    /// </summary>
                    public string Id { get; set; }

                    public static Task<string> HandleAsync(GetThingQuery op) => Task.FromResult("ok");
                }
            }
            """;

        var result = TestHelpers.Compile<OperationDescriptorSourceGenerator>(source);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output.Select(static t => t.ToString())
            .Single(static s => s.Contains("GetThingQueryDescriptor"));

        descriptorSource.Should().Contain("OperationDocumentationFeatureData");
        descriptorSource.Should().Contain("null,"); // null summary
        descriptorSource.Should().Contain("The thing identifier.");
    }

    [Fact]
    public async Task Operation_with_xml_docs_generates_feature_data_in_descriptor()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                /// <summary>
                /// Retrieves a documented widget.
                /// </summary>
                /// <remarks>
                /// Includes related metadata for UI rendering.
                /// </remarks>
                [RootLink("/widgets/{Id}")]
                public partial class GetDocumentedWidgetQuery : IQuery<string>
                {
                    /// <summary>
                    /// The widget identifier.
                    /// </summary>
                    public string Id { get; set; }

                    /// <summary>
                    /// The locale used to format the documentation output.
                    /// </summary>
                    public string Locale { get; set; }

                    public static Task<string> HandleAsync(GetDocumentedWidgetQuery operation) => Task.FromResult("ok");
                }
            }
            """;

        var compilation = CreateCompilation(source);
        var driver = TestHelpers.CreateBlueprintSourceGeneratorDriver(new OperationDescriptorSourceGenerator());

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        diagnostics.Should().BeEmpty();
        outputCompilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        var descriptorSource = driver.GetRunResult().Results
            .Single()
            .GeneratedSources
            .Select(static source => source.SourceText.ToString())
            .Single(static source => source.Contains("GetDocumentedWidgetQueryDescriptor", StringComparison.Ordinal));

        descriptorSource.Should().Contain("OperationDocumentationFeatureData");
        descriptorSource.Should().Contain("Retrieves a documented widget.");
        descriptorSource.Should().Contain("Includes related metadata for UI rendering.");
        descriptorSource.Should().Contain("\"Id\"");
        descriptorSource.Should().Contain("The widget identifier.");
        descriptorSource.Should().Contain("\"Locale\"");
        descriptorSource.Should().Contain("The locale used to format the documentation output.");

        var outputText = new StringBuilder();
        outputText.AppendLine("GetDocumentedWidgetQueryDescriptor.g.cs:");
        outputText.AppendLine();
        outputText.AppendLine(descriptorSource);
        outputText.AppendLine();

        await Verify(outputText)
            .UseDirectory(DescriptorSnapshotsFolder);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview, documentationMode: DocumentationMode.Parse);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions, "Input0.cs");
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "OperationDocumentationModuleTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}

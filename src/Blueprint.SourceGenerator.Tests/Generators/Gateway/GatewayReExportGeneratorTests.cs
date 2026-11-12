using Blueprint.SourceGenerator.Generators.ApiExceptionStatusIndex;
using Blueprint.SourceGenerator.Generators.Descriptors;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Generators.Gateway;

/// <summary>
/// A gateway operation re-exports its inner operation's errors and documentation (Symprex/blueprint#67): the inner
/// operation's <c>ApiExceptionFactory</c> members are documented on the exposing operation's responses as though declared
/// there, less those a <c>[TranslateError]</c> answers with the gateway's own (D14), and an undocumented exposing operation
/// takes the inner operation's docs and description (D15).
/// </summary>
public class GatewayReExportGeneratorTests
{
    private const string InnerSource = """
        namespace Inner
        {
            using System.ComponentModel;
            using System.Net;
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            public class WidgetResult : ApiResource
            {
                public string Id { get; set; } = "";
            }

            /// <summary>Renames a widget.</summary>
            /// <remarks>Only the name changes.</remarks>
            [RootLink("/widgets/{Id}")]
            [HttpPut]
            [Description("Rename a widget.")]
            public partial class RenameWidgetCommand : ICommand<WidgetResult>
            {
                public static readonly ApiExceptionFactory NotFound = new("Widget not found", "widget_not_found", HttpStatusCode.NotFound);

                public static readonly ApiExceptionFactory Conflict = new("Widget renamed concurrently", "widget_conflict", 409);

                /// <summary>The widget to rename.</summary>
                public string Id { get; set; } = "";

                /// <summary>The widget's new name.</summary>
                public string Name { get; set; } = "";
            }
        }
        """;

    private const string ClientSource = """
        namespace Gateway
        {
            using Blueprint.HttpClient;

            [BlueprintClient]
            [BlueprintClientInclude(typeof(Inner.RenameWidgetCommand))]
            public partial class WidgetApi;
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

    private const string GatewaySource = """
        namespace Gateway
        {
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            [Expose<Inner.RenameWidgetCommand>]
            [AllowAnonymous]
            [RootLink("/gateway/widgets/{Id}")]
            [HttpPut]
            public partial class RenameGatewayWidgetCommand : ICommand<Inner.WidgetResult>;
        }
        """;

    private const string TranslatingGatewaySource = """
        namespace Gateway
        {
            using System.Net;
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            [Expose<Inner.RenameWidgetCommand>]
            [AllowAnonymous]
            [RootLink("/gateway/widgets/{Id}")]
            [HttpPut]
            [TranslateError("widget_not_found", nameof(WidgetGone))]
            public partial class RenameGatewayWidgetCommand : ICommand<Inner.WidgetResult>
            {
                public static readonly ApiExceptionFactory WidgetGone = new("Widget gone", "widget_gone", HttpStatusCode.Gone);
            }
        }
        """;

    private const string DocumentedGatewaySource = """
        namespace Gateway
        {
            using System.ComponentModel;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            /// <summary>Renames a widget through the gateway.</summary>
            [Expose<Inner.RenameWidgetCommand>]
            [AllowAnonymous]
            [RootLink("/gateway/widgets/{Id}")]
            [HttpPut]
            [Description("Rename a widget via the gateway.")]
            public partial class RenameGatewayWidgetCommand : ICommand<Inner.WidgetResult>;
        }
        """;

    private const string Descriptor = "Gateway.RenameGatewayWidgetCommand_Descriptor.g.cs";
    private const string ExposedProperties = "Gateway.RenameGatewayWidgetCommand_ExposedProperties.g.cs";

    private static readonly Lazy<(byte[] Image, byte[] Documentation)> _indexedInner = new(static () => EmitInner(InnerSource, withIndex: true));

    [Fact]
    public void An_exposed_operation_documents_its_inner_operations_factories_from_their_assemblys_index()
    {
        var run = Run([ClientSource, GatewaySource, AppSource], InnerReference());

        run.EmitErrors.Should().BeEmpty();
        run.GeneratorErrors.Should().BeEmpty();

        // The index's title and type, as literals: generated code reads nothing from the inner contract to document it.
        var descriptor = run.Source(Descriptor);
        descriptor.Should().Contain("new global::Blueprint.Model.OperationHttpResponseMetadata(404, true, global::System.Collections.Immutable.ImmutableArray.Create<string>(\"Blueprint.ApiException\")")
            .And.Contain("new global::Blueprint.Model.OperationProblemExample(\"Widget not found\", \"widget_not_found\")")
            .And.Contain("new global::Blueprint.Model.OperationHttpResponseMetadata(409, true")
            .And.Contain("new global::Blueprint.Model.OperationProblemExample(\"Widget renamed concurrently\", \"widget_conflict\")");

        run.DescriptorAndAppReplayedFromCache.Should().BeTrue();
    }

    [Fact]
    public void An_inner_factory_a_TranslateError_answers_is_not_documented_and_the_gateways_own_is()
    {
        var run = Run([ClientSource, TranslatingGatewaySource, AppSource], InnerReference());

        run.EmitErrors.Should().BeEmpty();

        var descriptor = run.Source(Descriptor);
        descriptor.Should().NotContain("widget_not_found")
            .And.NotContain("OperationHttpResponseMetadata(404,")
            .And.Contain("new global::Blueprint.Model.OperationProblemExample(\"Widget renamed concurrently\", \"widget_conflict\")")
            .And.Contain("new global::Blueprint.Model.OperationProblemExample(global::Gateway.RenameGatewayWidgetCommand.WidgetGone.Title, global::Gateway.RenameGatewayWidgetCommand.WidgetGone.Type)");
    }

    [Fact]
    public void An_inner_factory_its_assembly_indexed_by_status_alone_is_read_from_its_field()
    {
        // An inner contract built by an earlier generator indexes the status only. The field is public, so the descriptor
        // reads its title and type at run time, as it does a factory the operation declares.
        const string legacyIndex = """
            namespace Blueprint.Model.Attributes
            {
                [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
                internal sealed class BlueprintApiExceptionStatusAttribute : System.Attribute
                {
                    public BlueprintApiExceptionStatusAttribute(string containingTypeMetadataName, string fieldName, int httpStatusCode) { }
                }
            }
            """;
        var source = "[assembly: Blueprint.Model.Attributes.BlueprintApiExceptionStatus(\"Inner.RenameWidgetCommand\", \"NotFound\", 404)]\n"
            + "[assembly: Blueprint.Model.Attributes.BlueprintApiExceptionStatus(\"Inner.RenameWidgetCommand\", \"Conflict\", 409)]\n"
            + legacyIndex + InnerSource;
        var (image, _) = EmitInner(source, withIndex: false);

        var run = Run([ClientSource, GatewaySource, AppSource], MetadataReference.CreateFromImage(image));

        run.EmitErrors.Should().BeEmpty();
        run.Source(Descriptor).Should().Contain("new global::Blueprint.Model.OperationProblemExample(global::Inner.RenameWidgetCommand.NotFound.Title, global::Inner.RenameWidgetCommand.NotFound.Type)");
    }

    [Fact]
    public void An_inner_operation_in_the_same_compilation_has_even_its_private_factories_documented_from_their_initialisers()
    {
        var sameCompilationInner = InnerSource.Replace("public static readonly ApiExceptionFactory NotFound", "private static readonly ApiExceptionFactory NotFound", StringComparison.Ordinal);

        var run = Run([sameCompilationInner, ClientSource, GatewaySource, AppSource], innerContract: null);

        run.EmitErrors.Should().BeEmpty();
        run.Source(Descriptor).Should().Contain("new global::Blueprint.Model.OperationProblemExample(\"Widget not found\", \"widget_not_found\")");
    }

    [Fact]
    public void An_undocumented_exposed_operation_inherits_its_inner_operations_docs_and_description()
    {
        var run = Run([ClientSource, GatewaySource, AppSource], InnerReference());

        run.EmitErrors.Should().BeEmpty();

        // The inheritdoc reaches the gateway's documentation file, where the runtime resolves it against the inner
        // contract's: Roslyn gives no documentation for a project reference's metadata (D15's spike).
        run.Source(ExposedProperties).ReplaceLineEndings("\n").Should().Contain("/// <inheritdoc cref=\"global::Inner.RenameWidgetCommand\"/>\n[global::System.ComponentModel.DescriptionAttribute(\"Rename a widget.\")]\npartial class RenameGatewayWidgetCommand");
    }

    [Fact]
    public void A_documented_exposed_operation_keeps_its_own_docs_and_description()
    {
        var run = Run([ClientSource, DocumentedGatewaySource, AppSource], InnerReference());

        run.EmitErrors.Should().BeEmpty();

        run.Source(ExposedProperties).Should().NotContain("<inheritdoc cref=\"global::Inner.RenameWidgetCommand\"/>")
            .And.NotContain("Rename a widget.");
        run.Source(Descriptor).Should().Contain("\"Renames a widget through the gateway.\"");
    }

    [Fact]
    public void Where_Roslyn_has_the_inner_operations_documentation_an_undocumented_exposed_operation_takes_its_summary_and_remarks()
    {
        // A package reference brings its documentation file, which Roslyn reads for the inner operation's symbol.
        var (image, documentation) = _indexedInner.Value;
        var reference = MetadataReference.CreateFromImage(image, documentation: XmlDocumentationProvider.CreateFromBytes(documentation));

        var run = Run([ClientSource, GatewaySource, AppSource], reference);

        run.EmitErrors.Should().BeEmpty();
        run.Source(Descriptor).Should().Contain("new global::Blueprint.Model.OperationDocumentationFeatureData(\"Renames a widget.\", \"Only the name changes.\"");
    }

    [Fact]
    public void An_inner_factory_written_with_named_arguments_in_another_order_is_documented_by_parameter()
    {
        // Read by position, the title and type would swap, and a [TranslateError] of the real type would match nothing.
        var namedInner = InnerSource.Replace(
            "new(\"Widget not found\", \"widget_not_found\", HttpStatusCode.NotFound)",
            "new(type: \"widget_not_found\", httpStatus: HttpStatusCode.NotFound, title: \"Widget not found\")",
            StringComparison.Ordinal);

        var run = Run([namedInner, ClientSource, GatewaySource, AppSource], innerContract: null);

        run.EmitErrors.Should().BeEmpty();
        run.Source(Descriptor).Should().Contain("new global::Blueprint.Model.OperationProblemExample(\"Widget not found\", \"widget_not_found\")")
            .And.Contain("OperationHttpResponseMetadata(404,");
    }

    [Fact]
    public void An_inner_factory_aliasing_a_custom_named_factory_of_a_third_assembly_is_documented_from_that_assemblys_index()
    {
        // Shared errors -> inner contract -> gateway: the inner contract's index has to follow the alias into the shared
        // assembly's index, as no HttpStatusCode is named MissingStripeAccount.
        const string sharedSource = """
            namespace SharedErrors
            {
                using System.Net;
                using Blueprint;

                public static class BillingApiExceptions
                {
                    public static readonly ApiExceptionFactory MissingStripeAccount = new("Missing Stripe account", "missing_stripe_account", HttpStatusCode.NotFound);
                }
            }
            """;
        var (sharedImage, _) = EmitInner(sharedSource, withIndex: true, assemblyName: "SharedErrors");
        var shared = MetadataReference.CreateFromImage(sharedImage);

        var aliasingInner = InnerSource.Replace(
            "new(\"Widget not found\", \"widget_not_found\", HttpStatusCode.NotFound)",
            "SharedErrors.BillingApiExceptions.MissingStripeAccount",
            StringComparison.Ordinal);
        var (innerImage, _) = EmitInner(aliasingInner, withIndex: true, references: [shared]);

        var run = Run([ClientSource, GatewaySource, AppSource], MetadataReference.CreateFromImage(innerImage), shared);

        run.EmitErrors.Should().BeEmpty();
        run.Source(Descriptor).Should().Contain("new global::Blueprint.Model.OperationProblemExample(\"Missing Stripe account\", \"missing_stripe_account\")");
    }

    [Fact]
    public void An_edit_to_an_inner_factorys_title_in_another_file_re_plans_the_exposing_operations_descriptor_on_the_same_driver()
    {
        var result = EditInner(InnerSource.Replace("\"Widget not found\"", "\"Widget is missing\"", StringComparison.Ordinal));

        result.GetChangedGeneratedSourceHintNames(nameof(OperationDescriptorSourceGenerator)).Should().Contain(Descriptor);
        UpdatedDescriptor(result).Should().Contain("OperationProblemExample(\"Widget is missing\", \"widget_not_found\")");
    }

    [Fact]
    public void An_edit_to_an_inner_operations_summary_in_another_file_re_plans_the_exposing_operations_descriptor_on_the_same_driver()
    {
        var result = EditInner(InnerSource.Replace("Renames a widget.", "Renames a widget, keeping its id.", StringComparison.Ordinal));

        result.GetChangedGeneratedSourceHintNames(nameof(OperationDescriptorSourceGenerator)).Should().Contain(Descriptor);
        UpdatedDescriptor(result).Should().Contain("\"Renames a widget, keeping its id.\"");
    }

    private static MultiGeneratorEditResult EditInner(string updatedInner)
    {
        var result = TestHelpers.CompileWithTrackedGeneratorsAfterEdit(
            [TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>()],
            [InnerSource, ClientSource, GatewaySource],
            [updatedInner, ClientSource, GatewaySource],
            ["Inner.cs", "Client.cs", "Gateway.cs"]);

        result.Updated.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        return result;
    }

    private static string UpdatedDescriptor(MultiGeneratorEditResult result)
    {
        return result.Updated.GetGeneratedSources(nameof(OperationDescriptorSourceGenerator)).Single(static s => s.HintName == Descriptor).SourceText;
    }

    private static ExposeGeneratorTests.AllGeneratorsRun Run(string[] sources, MetadataReference? innerContract, params MetadataReference[] additionalReferences)
    {
        // The inner operation in source still needs a reference standing in for the default inner contract.
        return ExposeGeneratorTests.RunAllGenerators(
            sources,
            innerContract ?? TestHelpers.CreateReferenceFromSource("namespace Unused { public class Nothing; }", "InnerContract"),
            additionalReferences);
    }

    private static MetadataReference InnerReference()
    {
        return MetadataReference.CreateFromImage(_indexedInner.Value.Image);
    }

    /// <summary>
    /// Builds the inner contract as a consumer's is built: with the descriptor generator, the status index generator when
    /// <paramref name="withIndex"/> is set, and its documentation file.
    /// </summary>
    private static (byte[] Image, byte[] Documentation) EmitInner(string source, bool withIndex, string assemblyName = "InnerContract", MetadataReference[]? references = null)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Diagnose);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TestHelpers.CreateMetadataReferences().Concat(references ?? []),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        IIncrementalGenerator[] generators = withIndex
            ? [new OperationDescriptorSourceGenerator(), new ApiExceptionStatusIndexGenerator()]
            : [new OperationDescriptorSourceGenerator()];

        CSharpGeneratorDriver.Create(generators.Select(static g => g.AsSourceGenerator()), parseOptions: parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var generated, out _);

        using var image = new MemoryStream();
        using var documentation = new MemoryStream();
        var emit = generated.Emit(image, xmlDocumentationStream: documentation);
        emit.Success.Should().BeTrue(string.Join(Environment.NewLine, emit.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));

        return (image.ToArray(), documentation.ToArray());
    }
}

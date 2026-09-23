using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;

/// <summary>
/// Proves that <see cref="BlueprintAppOperationNamesAttribute"/> on <see cref="IntegrationTestBlueprintApp"/>
/// (<c>Remove = "Sample"</c>) renames the operations the app hosts for every reader of the descriptor name — the
/// registered descriptor, links, OpenAPI <c>operationId</c>, request-body schema keys and <c>x-links</c> — with legacy
/// <c>IOperationScannerConvention</c> semantics, while resource schema names and the contract's own descriptor keep the
/// original name.
/// </summary>
public class AppModelOperationNameTransformTests
{
    private const string WidgetPath = "/operation-names/sample-widgets/{Id}";
    private const string WidgetCopiesPath = "/operation-names/sample-widgets/{Id}/copies";

    [Fact]
    public void When_app_removes_substring_then_registered_descriptor_is_named_without_it()
    {
        var app = new IntegrationTestBlueprintApp();

        app.FindOperation(typeof(SampleFooQuery)).Name.Should().Be("Foo");
    }

    [Fact]
    public void When_substring_occurs_more_than_once_then_every_occurrence_is_removed()
    {
        var app = new IntegrationTestBlueprintApp();

        app.FindOperation(typeof(SampleCopySampleWidgetCommand)).Name.Should().Be("CopyWidget");
    }

    [Fact]
    public void When_removal_exposes_a_Command_suffix_then_it_is_kept_because_suffix_stripping_ran_first()
    {
        var app = new IntegrationTestBlueprintApp();

        // Legacy stripped "ExportCommandSample" (no suffix to strip) and only then removed "Sample".
        app.FindOperation(typeof(ExportCommandSample)).Name.Should().Be("ExportCommand");
    }

    [Fact]
    public void When_app_renames_an_operation_then_the_contract_descriptor_keeps_its_own_name()
    {
        _ = new IntegrationTestBlueprintApp();

        SampleFooQuery.Configure().Name.Should().Be(
            "SampleFoo",
            "the rename belongs to the hosting app, so another app hosting the same operation must not see it");
    }

    [Fact]
    public void When_app_renames_an_operation_then_links_to_it_carry_the_app_name()
    {
        var app = new IntegrationTestBlueprintApp();

        app.GetLinksForResource(typeof(SampleWidgetApiResource))
            .Select(static link => (link.Rel, link.OperationDescriptor.Name))
            .Should()
            .BeEquivalentTo([("self", "Foo"), ("copy", "CopyWidget")]);
    }

    [Fact]
    public void When_openapi_document_is_rendered_then_operation_ids_use_the_app_names()
    {
        var paths = RenderDocument().RootElement.GetProperty("paths");

        paths.GetProperty(WidgetPath).GetProperty("get").GetProperty("operationId").GetString().Should().Be("Foo");
        paths.GetProperty(WidgetCopiesPath).GetProperty("post").GetProperty("operationId").GetString().Should().Be("CopyWidget");
    }

    [Fact]
    public void When_openapi_document_is_rendered_then_request_body_schema_is_keyed_by_the_app_name()
    {
        var document = RenderDocument().RootElement;

        document.GetProperty("paths")
            .GetProperty(WidgetCopiesPath)
            .GetProperty("post")
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema")
            .GetProperty("$ref")
            .GetString()
            .Should()
            .Be("#/components/schemas/CopyWidget");

        SchemaNames(document).Should().Contain("CopyWidget").And.NotContain("SampleCopySampleWidget");
    }

    [Fact]
    public void When_openapi_document_is_rendered_then_resource_schema_keeps_the_removed_substring()
    {
        var document = RenderDocument().RootElement;

        SchemaNames(document).Should().Contain("SampleWidgetApiResource").And.NotContain("WidgetApiResource");

        document.GetProperty("paths")
            .GetProperty(WidgetPath)
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema")
            .GetProperty("$ref")
            .GetString()
            .Should()
            .Be("#/components/schemas/SampleWidgetApiResource");
    }

    [Fact]
    public void When_document_links_are_enriched_from_the_app_then_x_links_use_the_app_operation_names()
    {
        // The component is keyed the way EnrichWithResourceLinks matches it — by the resource's CLR full name — so the
        // link-extension reader is exercised against the app's real links rather than through schema projection.
        var documentContext = CreateDocumentContextWithComponent(
            new IntegrationTestBlueprintApp(),
            typeof(SampleWidgetApiResource).FullName!);

        documentContext.SchemaComponents
            .Single(static component => component.BodyTypeName == typeof(SampleWidgetApiResource).FullName)
            .Links
            .Should()
            .Equal(new OpenApiSchemaLinkExtension("copy", "CopyWidget", "POST"));
    }

    private static JsonDocument RenderDocument()
    {
        var documentBuilderContext = OpenApiDocumentBuilderContext.Create(new IntegrationTestBlueprintApp());
        var schemaProjectionContext = OpenApiSchemaProjectionContext.Create(documentBuilderContext);
        var responseSchemaProjectionContext = OpenApiResponseSchemaReferenceProjectionContext.Create(schemaProjectionContext);
        var responseObjectContext = OpenApiResponseObjectRenderingContext.Create(responseSchemaProjectionContext);
        var operationContext = OpenApiOperationRenderingContext.Create(responseObjectContext);
        var pathItemContext = OpenApiPathItemRenderingContext.Create(operationContext);
        var documentContext = OpenApiDocumentRenderingContext.Create(pathItemContext);

        return JsonDocument.Parse(OpenApiOutputRenderingContext.Create(documentContext).Json);
    }

    private static OpenApiDocumentRenderingContext CreateDocumentContextWithComponent(BlueprintApp app, string bodyTypeName)
    {
        var documentBuilderContext = new OpenApiDocumentBuilderContext(
            app,
            null,
            ImmutableArray<string>.Empty,
            ImmutableArray<OpenApiDocumentOperationContext>.Empty);
        var schemaProjectionContext = new OpenApiSchemaProjectionContext(documentBuilderContext, ImmutableArray<OpenApiSchemaModel>.Empty, FrozenDictionary<string, string>.Empty);
        var schemaComponentContext = new OpenApiSchemaComponentRenderingContext(
            schemaProjectionContext,
            [new OpenApiSchemaComponentModel(bodyTypeName, "object", ImmutableArray<string>.Empty, ImmutableArray<OpenApiSchemaComponentPropertyModel>.Empty)]);
        var responseSchemaProjectionContext = new OpenApiResponseSchemaReferenceProjectionContext(
            schemaProjectionContext,
            ImmutableArray<OpenApiResponseSchemaReferenceOperationContext>.Empty);
        var responseObjectContext = new OpenApiResponseObjectRenderingContext(
            responseSchemaProjectionContext,
            schemaComponentContext,
            ImmutableArray<OpenApiResponseObjectOperationContext>.Empty);
        var operationContext = new OpenApiOperationRenderingContext(responseObjectContext, ImmutableArray<OpenApiOperationModel>.Empty);

        return OpenApiDocumentRenderingContext.Create(new OpenApiPathItemRenderingContext(operationContext, []));
    }

    private static string[] SchemaNames(JsonElement document)
    {
        return document.GetProperty("components")
            .GetProperty("schemas")
            .EnumerateObject()
            .Select(static schema => schema.Name)
            .ToArray();
    }
}

// --- Fixtures, hosted by IntegrationTestBlueprintApp, which removes "Sample" from operation names ---

/// <summary>A resource whose name contains the removed substring; resource schema names are never renamed.</summary>
public partial class SampleWidgetApiResource : ApiResource
{
    public string Id { get; set; } = null!;

    public string Name { get; set; } = null!;
}

/// <summary>Named <c>SampleFoo</c> by its contract and <c>Foo</c> by an app that removes "Sample".</summary>
[AllowAnonymous]
[SelfLink(typeof(SampleWidgetApiResource), "/operation-names/sample-widgets/{Id}")]
public partial class SampleFooQuery : IQuery<SampleWidgetApiResource>
{
    public string Id { get; set; } = "widget-1";

    public static Task<SampleWidgetApiResource> HandleAsync(SampleFooQuery query)
    {
        return Task.FromResult(new SampleWidgetApiResource { Id = query.Id, Name = "Widget" });
    }
}

/// <summary>
/// Contains the removed substring twice, and binds <see cref="NewName"/> from the body, so the renamed request-body
/// schema key and the resource's non-self <c>copy</c> link are both observable.
/// </summary>
[AllowAnonymous]
[Link(typeof(SampleWidgetApiResource), "/operation-names/sample-widgets/{Id}/copies", "copy")]
[HttpPost]
public partial class SampleCopySampleWidgetCommand : ICommand<SampleWidgetApiResource>
{
    public string Id { get; set; } = null!;

    public string NewName { get; set; } = null!;

    public static Task<SampleWidgetApiResource> HandleAsync(SampleCopySampleWidgetCommand command)
    {
        return Task.FromResult(new SampleWidgetApiResource { Id = command.Id, Name = command.NewName });
    }
}

/// <summary>
/// Has no <c>Query</c>/<c>Command</c> suffix until "Sample" is removed, pinning that the removal runs after suffix
/// stripping, as legacy did.
/// </summary>
[AllowAnonymous]
[RootLink("/operation-names/export-command-sample")]
public partial class ExportCommandSample : IQuery<string>
{
    public static Task<string> HandleAsync(ExportCommandSample query)
    {
        return Task.FromResult("exported");
    }
}

using System.Net;
using System.Text.Json;
using Blueprint.HttpClient;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Gateway operations re-export their inner operation's errors and documentation (Symprex/blueprint#67, D14 and D15): an
/// <c>[Expose&lt;TInner&gt;]</c> operation over <see cref="UpdateTemplateCommand"/>, from a referenced contract assembly,
/// documents its inner <c>ApiExceptionFactory</c> errors and its docs as though it declared them, less any inner error a
/// <c>[TranslateError]</c> answers with one of its own.
/// </summary>
public sealed class GatewayOpenApiTests
{
    private const string ExposedPath = "/gateway-openapi/templates/{Id}";
    private const string TranslatedPath = "/gateway-openapi/translated/templates/{Id}";

    [Fact]
    public void An_exposed_operation_documents_its_inner_operations_404_and_409_errors()
    {
        using var document = Render(new OpenApiInfoOptions());

        StatusCodes(document, ExposedPath).Should().Contain(["404", "409"]);
    }

    [Fact]
    public void An_exposed_operation_documents_its_inner_errors_titles_and_types_as_legacy_did()
    {
        using var document = Render(new OpenApiInfoOptions { SchemaNaming = OpenApiSchemaNaming.Legacy });

        var notFound = Response(document, ExposedPath, "404");
        notFound.GetProperty("description").GetString().Should().Be("Template not found");
        LegacyExample(notFound, "TemplateNotFound").GetProperty("type").GetString().Should().Be("template_not_found");

        var conflict = Response(document, ExposedPath, "409");
        conflict.GetProperty("description").GetString().Should().Be("Template version conflict");
        LegacyExample(conflict, "TemplateConflict").GetProperty("title").GetString().Should().Be("Template version conflict");
    }

    [Fact]
    public void An_inner_error_a_TranslateError_answers_is_replaced_by_the_gateways_own()
    {
        using var document = Render(new OpenApiInfoOptions { SchemaNaming = OpenApiSchemaNaming.Legacy });

        StatusCodes(document, TranslatedPath).Should().NotContain("404").And.Contain(["409", "410"]);
        Response(document, TranslatedPath, "410").GetProperty("description").GetString().Should().Be("Gateway template gone");
    }

    [Fact]
    public void An_undocumented_exposed_operation_takes_its_inner_operations_description_as_its_summary()
    {
        using var document = Render(new OpenApiInfoOptions());

        Operation(document, ExposedPath).GetProperty("summary").GetString().Should().Be("Update an existing template's name and content.");
    }

    [Fact]
    public void An_undocumented_exposed_operation_takes_its_inner_operations_xml_summary_from_the_inner_documentation_file()
    {
        // Legacy read an operation's summary from the XML documentation beside its assembly. The gateway's file holds only
        // the generated <inheritdoc cref>, which is resolved against the inner contract's file.
        using var document = Render(new OpenApiInfoOptions { SchemaNaming = OpenApiSchemaNaming.Legacy });

        Operation(document, ExposedPath).GetProperty("summary").GetString().Should().StartWith("Updates an existing template's metadata and content.");
    }

    [Fact]
    public void An_exposed_operations_properties_keep_their_inner_descriptions()
    {
        using var document = Render(new OpenApiInfoOptions());

        var description = Operation(document, ExposedPath).GetProperty("parameters").EnumerateArray()
            .Single(static parameter => parameter.GetProperty("name").GetString() == "Id")
            .GetProperty("description").GetString();

        description.Should().Be("The unique identifier of the template to update.");
    }

    [Fact]
    public void An_exposed_operations_generated_property_reads_its_inner_propertys_xml_summary_through_its_inheritdoc_cref()
    {
        // No base type or interface of the gateway operation has the property, so only the cref can name where its
        // documentation is: the inner contract's file, which Roslyn did not read for the project reference.
        var property = typeof(UpdateGatewayOpenApiTemplateCommand).GetProperty(nameof(UpdateTemplateCommand.Name))!;

        OpenApiXmlDocs.GetSchemaSummary(property).Should().Be("The updated name for the template. Required and limited to 200 characters.");
    }

    private static JsonElement LegacyExample(JsonElement response, string key)
    {
        return response.GetProperty("content").GetProperty("application/problem+json").GetProperty("example").GetProperty(key).GetProperty("value");
    }

    private static List<string> StatusCodes(JsonDocument document, string path)
    {
        return Operation(document, path).GetProperty("responses").EnumerateObject().Select(static response => response.Name).ToList();
    }

    private static JsonElement Response(JsonDocument document, string path, string status)
    {
        return Operation(document, path).GetProperty("responses").GetProperty(status);
    }

    private static JsonElement Operation(JsonDocument document, string path)
    {
        return document.RootElement.GetProperty("paths").GetProperty(path).GetProperty("put");
    }

    private static JsonDocument Render(OpenApiInfoOptions options)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), options);

        return JsonDocument.Parse(rendered.Json);
    }
}

/// <summary>The upstream template service, as the gateway operations below forward to it.</summary>
[BlueprintClient]
[BlueprintClientInclude(typeof(UpdateTemplateCommand))]
internal partial class GatewayOpenApiTemplateApi;

// Undocumented on purpose: its summary, its description and its errors all come from the inner operation.
#pragma warning disable SA1600, CS1591
[Expose<UpdateTemplateCommand>]
[AllowAnonymous]
[RootLink("/gateway-openapi/templates/{Id}")]
[HttpPut]
public partial class UpdateGatewayOpenApiTemplateCommand : ICommand<ResourceEventResult<TemplateApiResource>>;
#pragma warning restore SA1600, CS1591

/// <summary>Updates a template upstream, answering the upstream's <c>template_not_found</c> as the gateway's own 410.</summary>
[Expose<UpdateTemplateCommand>]
[AllowAnonymous]
[RootLink("/gateway-openapi/translated/templates/{Id}")]
[HttpPut]
[TranslateError("template_not_found", nameof(TemplateGone))]
public partial class UpdateTranslatedGatewayOpenApiTemplateCommand : ICommand<ResourceEventResult<TemplateApiResource>>
{
    /// <summary>The gateway's answer to the upstream's <c>template_not_found</c>.</summary>
    public static readonly ApiExceptionFactory TemplateGone = new("Gateway template gone", "gateway_template_gone", HttpStatusCode.Gone);
}

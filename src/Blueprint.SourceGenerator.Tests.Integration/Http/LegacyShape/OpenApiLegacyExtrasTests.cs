using System.Text.Json;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves that <c>title</c> on a schema component and <c>readOnly</c> on a property — neither of which legacy Blueprint
/// ever wrote (checked against every Signature365 8b40ecc961 app's committed <c>Spec/openapi.json</c>: zero components
/// with <c>title</c>, zero properties with <c>readOnly</c>, in all 21 documents) — are omitted only under
/// <see cref="OpenApiSchemaNaming.Legacy"/>. A <see cref="OpenApiSchemaNaming.Default"/> consumer keeps both: they are
/// real, useful facts (a friendly component name; a property with no public setter) that legacy simply never had the
/// means to describe (NJsonSchema's <c>DefaultSchemaNameGenerator</c> title and Newtonsoft's contract resolution did not
/// track either), not something Default was wrong to add.
/// </summary>
public class OpenApiLegacyExtrasTests
{
    [Fact]
    public void Legacy_naming_omits_title_and_readOnly()
    {
        using var document = Render(static options => options.SchemaNaming = OpenApiSchemaNaming.Legacy);

        var tenant = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("LegacyShapeTenantApiResource");
        tenant.TryGetProperty("title", out _).Should().BeFalse("legacy never wrote a schema component's title");

        var run = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("LegacyShapeRun");
        run.GetProperty("properties").GetProperty("$object").TryGetProperty("readOnly", out _)
            .Should().BeFalse("legacy never wrote readOnly, even for a property (Object) with no public setter");
    }

    [Fact]
    public void Default_naming_still_carries_title_and_readOnly()
    {
        using var document = Render(static _ => { });

        var tenant = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("LegacyShapeTenantApiResource");
        tenant.GetProperty("title").GetString().Should().Be("LegacyShapeTenantApiResource");

        var run = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("LegacyShapeRun");
        run.GetProperty("properties").GetProperty("$object").GetProperty("readOnly").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Legacy_naming_omits_a_request_bodys_required_flag()
    {
        // Legacy's generator never wrote requestBody.required: every one of the 97 request bodies in Signature365's
        // 8b40ecc961 Portal document carries "content" alone.
        using var document = Render(static options => options.SchemaNaming = OpenApiSchemaNaming.Legacy);

        BoundedDescriptionRequestBody(document).TryGetProperty("required", out _)
            .Should().BeFalse("legacy never wrote a request body's required flag");
    }

    [Fact]
    public void Default_naming_still_marks_a_request_body_required()
    {
        using var document = Render(static _ => { });

        BoundedDescriptionRequestBody(document).GetProperty("required").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Legacy_naming_omits_the_synthetic_415_response()
    {
        // No document legacy wrote for Signature365 at 8b40ecc961 has a 415 response (all 21 checked): legacy read
        // every non-JSON body by ignoring it, bar the form types, and documented no such response.
        using var document = Render(static options => options.SchemaNaming = OpenApiSchemaNaming.Legacy);

        BoundedDescriptionPost(document).GetProperty("responses").TryGetProperty("415", out _)
            .Should().BeFalse("legacy never documented a 415");
    }

    [Fact]
    public void Default_naming_still_documents_the_415_response()
    {
        // A form content type is still refused with a 415 (R117), so a Default consumer's document keeps it.
        using var document = Render(static _ => { });

        BoundedDescriptionPost(document).GetProperty("responses").TryGetProperty("415", out _).Should().BeTrue();
    }

    private static JsonElement BoundedDescriptionRequestBody(JsonDocument document)
    {
        return BoundedDescriptionPost(document).GetProperty("requestBody");
    }

    private static JsonElement BoundedDescriptionPost(JsonDocument document)
    {
        var path = document.RootElement.GetProperty("paths").EnumerateObject()
            .Single(static p => p.Name.EndsWith("legacy-shape/bounded-description", StringComparison.Ordinal));

        return path.Value.GetProperty("post");
    }

    private static JsonDocument Render(Action<OpenApiInfoOptions> configure)
    {
        var options = new OpenApiInfoOptions();
        configure(options);

        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), options);

        return JsonDocument.Parse(rendered.Json);
    }
}

using System.Text.Json;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves that the <c>Link</c> component is closed to further properties in both namings, as every legacy component was,
/// and that under Legacy naming it is written only when a component references it, while Default naming always writes it.
/// </summary>
/// <remarks>
/// In Signature365's 18 legacy documents (<c>8b40ecc961</c>) all 14 <c>Link</c> components have
/// <c>additionalProperties: false</c>, like every other object component, and it is absent from the four documents
/// (AiGateway, Microsoft365, Microsoft365.Gateway, VCard.Gateway) whose components have no <c>$links</c>: NJsonSchema
/// wrote a component only when something reached it.
/// </remarks>
public sealed class OpenApiLinkComponentTests
{
    [Theory]
    [InlineData(OpenApiSchemaNaming.Default)]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    public void The_link_component_allows_no_further_properties(OpenApiSchemaNaming naming)
    {
        using var document = Render(naming, stripLinks: false);

        Schemas(document).GetProperty("Link").GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void Under_legacy_naming_the_link_component_is_written_when_a_component_references_it()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy, stripLinks: false);

        Schemas(document).TryGetProperty("Link", out _).Should().BeTrue();
        Schemas(document).GetProperty("LegacyShapeTenantApiResource").GetProperty("properties").GetProperty("$links")
            .GetProperty("additionalProperties").GetProperty("$ref").GetString().Should().Be("#/components/schemas/Link");
    }

    [Fact]
    public void Under_legacy_naming_the_link_component_is_left_out_when_no_component_references_it()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy, stripLinks: true);

        Schemas(document).TryGetProperty("Link", out _).Should().BeFalse();
        Schemas(document).TryGetProperty("LegacyShapeTenantApiResource", out _).Should().BeTrue("the other components are still written");
    }

    [Fact]
    public void Under_default_naming_the_link_component_is_written_even_when_no_component_references_it()
    {
        using var document = Render(OpenApiSchemaNaming.Default, stripLinks: true);

        Schemas(document).TryGetProperty("Link", out _).Should().BeTrue();
    }

    private static JsonElement Schemas(JsonDocument document)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas");
    }

    private static JsonDocument Render(OpenApiSchemaNaming naming, bool stripLinks)
    {
        var processors = new OpenApiSchemaProcessorOptions();

        if (stripLinks)
        {
            // Takes every component's $links property away, so that nothing references Link.
            processors.Processors.Add(static context => context.Component = context.Component with
            {
                Properties = [.. context.Component.Properties.Where(static property => property.Name != "$links")],
            });
        }

        var (_, rendered) = OpenApiDocumentEndpoint.Create(
            new IntegrationTestBlueprintApp(),
            new OpenApiInfoOptions { SchemaNaming = naming },
            processors);

        return JsonDocument.Parse(rendered.Json);
    }
}

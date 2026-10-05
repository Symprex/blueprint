using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that an enum reached only as the items of an array, or the values of a dictionary, property of a response
/// resource has a schema component, so the <c>$ref</c> the property writes to it resolves.
/// </summary>
/// <remarks>
/// Signature365's <c>CompiledSignature.usages</c> (a <c>SignatureUsage[]</c>) and
/// <c>Microsoft365AddOn.authorisationRefreshRequired</c> wrote <c>items.$ref</c> to a component nothing collected,
/// because only a property's own enum was: the generated client cannot resolve the type.
/// </remarks>
public sealed class OpenApiEnumItemsComponentTests
{
    [Theory]
    [InlineData(OpenApiSchemaNaming.Default)]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    public void An_enum_reached_only_as_array_items_has_a_component(OpenApiSchemaNaming naming)
    {
        using var document = Render(naming);

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        schemas.GetProperty("EnumItemsResource").GetProperty("properties").GetProperty("Usages").GetProperty("items")
            .GetProperty("$ref").GetString().Should().Be("#/components/schemas/EnumItemsUsage");

        var usage = schemas.GetProperty("EnumItemsUsage");
        usage.GetProperty("type").GetString().Should().Be("string");
        usage.GetProperty("enum").EnumerateArray().Select(static value => value.GetString()).Should().Equal("Primary", "Internal");
        usage.GetProperty("description").GetString().Should().Be("How an items resource may be used.");
    }

    [Theory]
    [InlineData(OpenApiSchemaNaming.Default)]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    public void An_enum_reached_only_as_dictionary_values_has_a_component(OpenApiSchemaNaming naming)
    {
        using var document = Render(naming);

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        schemas.GetProperty("EnumItemsResource").GetProperty("properties").GetProperty("ModesByName").GetProperty("additionalProperties")
            .GetProperty("$ref").GetString().Should().Be("#/components/schemas/EnumItemsMode");
        schemas.GetProperty("EnumItemsMode").GetProperty("enum").EnumerateArray().Select(static value => value.GetString())
            .Should().Equal("Fast", "Slow");
    }

    [Theory]
    [InlineData(OpenApiSchemaNaming.Default)]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    public void An_enum_reached_only_as_the_items_of_dictionary_values_has_a_component(OpenApiSchemaNaming naming)
    {
        using var document = Render(naming);

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        schemas.GetProperty("EnumItemsResource").GetProperty("properties").GetProperty("KindsByName").GetProperty("additionalProperties")
            .GetProperty("items").GetProperty("$ref").GetString().Should().Be("#/components/schemas/EnumItemsKind");
        schemas.GetProperty("EnumItemsKind").GetProperty("enum").EnumerateArray().Select(static value => value.GetString())
            .Should().Equal("Small", "Large");
    }

    [Theory]
    [InlineData(OpenApiSchemaNaming.Default)]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    public void Every_component_reference_in_the_document_resolves(OpenApiSchemaNaming naming)
    {
        using var document = Render(naming);

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var dangling = new List<string>();

        CollectReferences(document.RootElement, dangling, schemas);

        dangling.Should().BeEmpty("every $ref into components/schemas must name a component the document writes");
    }

    private static void CollectReferences(JsonElement element, List<string> dangling, JsonElement schemas)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == "$ref" && property.Value.ValueKind == JsonValueKind.String
                        && property.Value.GetString() is { } reference
                        && reference.StartsWith("#/components/schemas/", StringComparison.Ordinal)
                        && !schemas.TryGetProperty(reference["#/components/schemas/".Length..], out _))
                    {
                        dangling.Add(reference);
                    }

                    CollectReferences(property.Value, dangling, schemas);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectReferences(item, dangling, schemas);
                }

                break;
        }
    }

    private static JsonDocument Render(OpenApiSchemaNaming naming)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(
            new IntegrationTestBlueprintApp(),
            new OpenApiInfoOptions { SchemaNaming = naming });

        return JsonDocument.Parse(rendered.Json);
    }
}

/// <summary>How an items resource may be used.</summary>
public enum EnumItemsUsage
{
    Primary,
    Internal,
}

/// <summary>How an items resource runs.</summary>
public enum EnumItemsMode
{
    Fast,
    Slow,
}

/// <summary>How big an items resource is.</summary>
public enum EnumItemsKind
{
    Small,
    Large,
}

/// <summary>Holds enums reached only through a collection and a dictionary, never as a property of their own.</summary>
public sealed class EnumItemsResource
{
    public EnumItemsUsage[]? Usages { get; set; }

    public Dictionary<string, EnumItemsMode>? ModesByName { get; set; }

    public Dictionary<string, EnumItemsKind[]>? KindsByName { get; set; }
}

/// <summary>Reads the resource holding the collection and the dictionary.</summary>
[AllowAnonymous]
[RootLink("/enum-items/resource")]
public partial class GetEnumItemsQuery : IQuery<EnumItemsResource>
{
    public static EnumItemsResource Handle(GetEnumItemsQuery query)
    {
        return new EnumItemsResource();
    }
}

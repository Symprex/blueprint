using System.Text.Json;
using System.Text.Json.Nodes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves the items of a collection whose element is a nullable value type (<c>double?[]</c>, <c>List&lt;int?&gt;</c>)
/// carry <c>"nullable": true</c>, as legacy Blueprint's document had them (Signature365's
/// <c>SurveyApiResource.ScoreTrend</c>), while the items of a collection of non-nullable value types stay as they were.
/// </summary>
public sealed class OpenApiNullableValueItemsTests
{
    [Theory]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    [InlineData(OpenApiSchemaNaming.Default)]
    public void The_items_of_an_array_of_a_nullable_value_type_are_nullable(OpenApiSchemaNaming naming)
    {
        using var document = Render(naming);

        Json(Resource(document, "ScoreTrend"))
            .Should().Be("""{"type":"array","nullable":true,"items":{"type":"number","format":"double","nullable":true}}""");
    }

    [Theory]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    [InlineData(OpenApiSchemaNaming.Default)]
    public void The_items_of_a_list_of_a_nullable_value_type_are_nullable(OpenApiSchemaNaming naming)
    {
        using var document = Render(naming);

        Json(Resource(document, "Counts").GetProperty("items"))
            .Should().Be("""{"type":"integer","format":"int32","nullable":true}""");
    }

    [Theory]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    [InlineData(OpenApiSchemaNaming.Default)]
    public void The_items_of_an_array_of_a_non_nullable_value_type_stay_without_nullable(OpenApiSchemaNaming naming)
    {
        using var document = Render(naming);

        Json(Resource(document, "Totals").GetProperty("items")).Should().Be("""{"type":"integer","format":"int32"}""");
    }

    [Theory]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    [InlineData(OpenApiSchemaNaming.Default)]
    public void A_request_body_has_the_same_nullable_and_non_nullable_value_items(OpenApiSchemaNaming naming)
    {
        using var document = Render(naming);
        var body = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("CreateRefWrapper").GetProperty("properties");

        Json(body.GetProperty("scoreTrend").GetProperty("items")).Should().Be("""{"type":"number","format":"double","nullable":true}""");
        Json(body.GetProperty("counts").GetProperty("items")).Should().Be("""{"type":"integer","format":"int32","nullable":true}""");
        Json(body.GetProperty("totals").GetProperty("items")).Should().Be("""{"type":"integer","format":"int32"}""");
    }

    [Theory]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    [InlineData(OpenApiSchemaNaming.Default)]
    public void The_items_of_an_inherited_collection_follow_its_IEnumerable_element_type(OpenApiSchemaNaming naming)
    {
        using var document = Render(naming);
        var body = Body(document);

        // LegacyShapeScores : List<double?> has nullable elements; LegacyShapeIntValues<double?> : List<int> does not,
        // whatever its type argument.
        Json(Resource(document, "Scores").GetProperty("items")).Should().Be("""{"type":"number","format":"double","nullable":true}""");
        Json(Resource(document, "IntValues").GetProperty("items")).Should().Be("""{"type":"integer","format":"int32"}""");
        Json(body.GetProperty("scores").GetProperty("items")).Should().Be("""{"type":"number","format":"double","nullable":true}""");
        Json(body.GetProperty("intValues").GetProperty("items")).Should().Be("""{"type":"integer","format":"int32"}""");
    }

    [Theory]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    [InlineData(OpenApiSchemaNaming.Default)]
    public void The_items_of_a_collection_of_a_nullable_struct_with_a_component_wrap_their_reference_beside_nullable(OpenApiSchemaNaming naming)
    {
        using var document = Render(naming);

        const string Expected = """{"nullable":true,"oneOf":[{"$ref":"#/components/schemas/LegacyShapeCoordinate"}]}""";
        Json(Resource(document, "Coordinates").GetProperty("items")).Should().Be(Expected);
        Json(Body(document).GetProperty("coordinates").GetProperty("items")).Should().Be(Expected);
    }

    private static JsonElement Body(JsonDocument document)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("CreateRefWrapper").GetProperty("properties");
    }

    private static string Json(JsonElement element)
    {
        return JsonNode.Parse(element.GetRawText())!.ToJsonString();
    }

    private static JsonElement Resource(JsonDocument document, string property)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("RefWrapperApiResource")
            .GetProperty("properties").GetProperty(property);
    }

    private static JsonDocument Render(OpenApiSchemaNaming naming)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(
            new IntegrationTestBlueprintApp(),
            new OpenApiInfoOptions { SchemaNaming = naming });

        return JsonDocument.Parse(rendered.Json);
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves that under Legacy naming the items of a collection of <see cref="Nullable{T}"/> elements carry
/// <c>nullable</c> after their type and format, as legacy Blueprint's document had them, while a collection of
/// elements that are not null keeps its items bare.
/// </summary>
/// <remarks>
/// Portal's <c>Survey.scoreTrend</c> (<c>double?[]</c>), whose entries are null for a window with no responses, was
/// written by legacy as <c>{type: number, format: double, nullable: true}</c> (<c>b7e496b3d0</c>). The framework wrote
/// the items without <c>nullable</c>, so the generated client typed the entries as never null.
/// </remarks>
public sealed class OpenApiLegacyNullablePrimitiveItemsTests
{
    [Fact]
    public void Under_legacy_naming_the_items_of_an_array_of_nullable_values_are_nullable()
    {
        using var document = Render();

        JsonNode.Parse(Property(document, "Scores").GetProperty("items").GetRawText())!.ToJsonString()
            .Should().Be("""{"type":"number","format":"double","nullable":true}""");
    }

    [Fact]
    public void Under_legacy_naming_the_items_of_a_list_of_nullable_values_are_nullable()
    {
        using var document = Render();

        JsonNode.Parse(Property(document, "Counts").GetProperty("items").GetRawText())!.ToJsonString()
            .Should().Be("""{"type":"integer","format":"int32","nullable":true}""");
    }

    [Fact]
    public void Under_legacy_naming_the_items_of_an_array_of_values_that_are_not_null_stay_bare()
    {
        using var document = Render();

        JsonNode.Parse(Property(document, "Totals").GetProperty("items").GetRawText())!.ToJsonString()
            .Should().Be("""{"type":"number","format":"double"}""");
    }

    [Fact]
    public void Under_legacy_naming_a_request_body_has_the_same_nullable_items()
    {
        using var document = Render();
        var body = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("CreateNullableItems").GetProperty("properties");

        JsonNode.Parse(body.GetProperty("ratings").GetProperty("items").GetRawText())!.ToJsonString()
            .Should().Be("""{"type":"integer","format":"int32","nullable":true}""");
        JsonNode.Parse(body.GetProperty("requiredRatings").GetProperty("items").GetRawText())!.ToJsonString()
            .Should().Be("""{"type":"integer","format":"int32"}""");
    }

    private static JsonElement Property(JsonDocument document, string property)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("NullableItemsApiResource")
            .GetProperty("properties").GetProperty(property);
    }

    private static JsonDocument Render()
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(
            new IntegrationTestBlueprintApp(),
            new OpenApiInfoOptions { SchemaNaming = OpenApiSchemaNaming.Legacy });

        return JsonDocument.Parse(rendered.Json);
    }
}

/// <summary>A resource with collections of nullable and of non-null values.</summary>
public partial class NullableItemsApiResource : ApiResource
{
    public double?[]? Scores { get; init; }

    public List<int?> Counts { get; init; } = [];

    public double[] Totals { get; init; } = [];
}

/// <summary>Returns the resource.</summary>
[AllowAnonymous]
[RootLink("/nullable-items")]
public partial class GetNullableItemsQuery : IQuery<NullableItemsApiResource>
{
    public static NullableItemsApiResource Handle(GetNullableItemsQuery query)
    {
        return new NullableItemsApiResource();
    }
}

/// <summary>A command whose body has the same forms.</summary>
[AllowAnonymous]
[RootLink("/nullable-items")]
[HttpPost]
public partial class CreateNullableItemsCommand : ICommand<string>
{
    public List<int?>? Ratings { get; set; }

    public List<int> RequiredRatings { get; set; } = [];

    public static string Handle(CreateNullableItemsCommand command)
    {
        return "created";
    }
}

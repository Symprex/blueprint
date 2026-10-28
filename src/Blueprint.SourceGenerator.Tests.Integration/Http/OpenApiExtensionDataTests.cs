using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a type with a <see cref="JsonExtensionDataAttribute"/> bucket is documented as legacy Blueprint documented
/// it: the bucket as a property of its own, and the type as open to further members of the bucket's value.
/// </summary>
/// <remarks>
/// <para>
/// Legacy described a type through Newtonsoft.Json, which knows nothing of System.Text.Json's
/// <see cref="JsonExtensionDataAttribute"/>, so its contract kept the bucket as an ordinary dictionary property. NJsonSchema
/// recognises the attribute by name, and made the schema's own <c>additionalProperties</c> the bucket's value: any value,
/// nullable when the value's annotation was.
/// </para>
/// <para>
/// Every expected shape here is legacy's, from NJsonSchema 11.1 run with legacy's settings (its
/// <c>BlueprintContractResolver</c>, <c>StringEnumConverter</c>, OpenAPI 3 and a flattened hierarchy) on the same types.
/// A problem's <c>extensions</c> keeps the shape Signature365's committed documents record for it.
/// </para>
/// </remarks>
public sealed class OpenApiExtensionDataTests
{
    [Theory]
    [InlineData("ExtensionBucketNullableValues", """{ "nullable": true }""")]
    [InlineData("ExtensionBucketNonNullValues", """{}""")]
    [InlineData("ExtensionBucketNonNullDictionary", """{ "nullable": true }""")]
    [InlineData("ExtensionBucketJsonElement", """{}""")]
    public void A_type_with_an_extension_bucket_is_open_to_members_of_the_bucket_value(string component, string expectedAdditionalProperties)
    {
        using var document = Render(OpenApiRequiredProperties.Declared);

        Normalise(Component(document, component).GetProperty("additionalProperties").GetRawText())
            .Should().Be(Normalise(expectedAdditionalProperties));
    }

    [Theory]
    [InlineData("ExtensionBucketNullableValues", "Extensions", """{ "type": "object", "nullable": true, "additionalProperties": {} }""")]
    [InlineData("ExtensionBucketNonNullValues", "Extensions", """{ "type": "object", "nullable": true, "additionalProperties": {} }""")]
    [InlineData("ExtensionBucketNonNullDictionary", "Extra", """{ "type": "object", "additionalProperties": {} }""")]
    [InlineData("ExtensionBucketJsonElement", "Rest", """{ "type": "object", "nullable": true, "additionalProperties": {} }""")]
    public void An_extension_bucket_is_documented_as_a_property_of_its_own(string component, string property, string expectedSchema)
    {
        using var document = Render(OpenApiRequiredProperties.Declared);
        var properties = Component(document, component).GetProperty("properties");

        properties.EnumerateObject().Select(static p => p.Name).Should().BeEquivalentTo("Status", property);
        Normalise(properties.GetProperty(property).GetRawText()).Should().Be(Normalise(expectedSchema));
    }

    [Theory]
    [InlineData("ExtensionBucketNullableValues", new[] { "Status" })]
    [InlineData("ExtensionBucketNonNullValues", new[] { "Status" })]
    [InlineData("ExtensionBucketNonNullDictionary", new[] { "Status", "Extra" })]
    [InlineData("ExtensionBucketJsonElement", new[] { "Status" })]
    public void Under_the_legacy_rule_a_bucket_whose_type_is_not_nullable_is_required(string component, string[] expectedRequired)
    {
        using var document = Render(OpenApiRequiredProperties.NonNullable);

        Component(document, component).GetProperty("required").EnumerateArray().Select(static name => name.GetString())
            .Should().BeEquivalentTo(expectedRequired);
    }

    [Fact]
    public void A_problem_keeps_its_documented_extensions()
    {
        using var document = Render(OpenApiRequiredProperties.Declared);
        var problem = Component(document, "ProblemDetails");

        Normalise(problem.GetProperty("additionalProperties").GetRawText()).Should().Be(Normalise("""{ "nullable": true }"""));
        Normalise(problem.GetProperty("properties").GetProperty("extensions").GetRawText())
            .Should().Be(Normalise("""{ "type": "object", "nullable": true, "additionalProperties": {} }"""));

        // Signature365's committed Partners document: the bucket appears once, as "extensions".
        problem.GetProperty("properties").EnumerateObject().Select(static p => p.Name)
            .Should().BeEquivalentTo("detail", "extensions", "instance", "status", "title", "type");
    }

    private static JsonDocument Render(OpenApiRequiredProperties requiredProperties)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(
            new IntegrationTestBlueprintApp(),
            new OpenApiInfoOptions { RequiredProperties = requiredProperties });

        return JsonDocument.Parse(rendered.Json);
    }

    private static JsonElement Component(JsonDocument document, string name)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(name);
    }

    /// <summary>Compact JSON with its members in name order, so two documents that differ only in order compare equal.</summary>
    private static string Normalise(string json)
    {
        return Sort(JsonNode.Parse(json))?.ToJsonString() ?? "null";

        static JsonNode? Sort(JsonNode? node)
        {
            return node switch
            {
                JsonObject obj => new JsonObject(obj.OrderBy(static p => p.Key, StringComparer.Ordinal).Select(static p => KeyValuePair.Create(p.Key, Sort(p.Value)))),
                JsonArray array => new JsonArray(array.Select(Sort).ToArray()),
                _ => node?.DeepClone(),
            };
        }
    }
}

/// <summary>A resource holding a type with an extension bucket of each shape legacy told apart.</summary>
public sealed class ExtensionShapeApiResource
{
    public ExtensionBucketNullableValues NullableValues { get; init; } = new();

    public ExtensionBucketNonNullValues NonNullValues { get; init; } = new();

    public ExtensionBucketNonNullDictionary NonNullDictionary { get; init; } = new();

    public ExtensionBucketJsonElement JsonElementValues { get; init; } = new();
}

/// <summary>A nullable bucket of nullable values.</summary>
public sealed class ExtensionBucketNullableValues
{
    public int Status { get; init; }

    // Schema-only: these values are never written, so only a registered primitive is declared (BP012).
    [JsonExtensionData]
    [BlueprintRuntimeTypes(typeof(string))]
    public IDictionary<string, object?>? Extensions { get; set; }
}

/// <summary>A nullable bucket of values that are not.</summary>
public sealed class ExtensionBucketNonNullValues
{
    public int Status { get; init; }

    // Schema-only: these values are never written, so only a registered primitive is declared (BP012).
    [JsonExtensionData]
    [BlueprintRuntimeTypes(typeof(string))]
    public IDictionary<string, object>? Extensions { get; set; }
}

/// <summary>A bucket that is not nullable, of nullable values.</summary>
public sealed class ExtensionBucketNonNullDictionary
{
    public int Status { get; init; }

    // Schema-only: these values are never written, so only a registered primitive is declared (BP012).
    [JsonExtensionData]
    [BlueprintRuntimeTypes(typeof(string))]
    public Dictionary<string, object?> Extra { get; set; } = [];
}

/// <summary>A nullable bucket of JSON elements.</summary>
public sealed class ExtensionBucketJsonElement
{
    public int Status { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Rest { get; set; }
}

/// <summary>Gets the shape.</summary>
[AllowAnonymous]
[RootLink("/extension-shapes")]
public partial class GetExtensionShapeQuery : IQuery<ExtensionShapeApiResource>
{
    public static ExtensionShapeApiResource Handle(GetExtensionShapeQuery query)
    {
        return new ExtensionShapeApiResource();
    }
}

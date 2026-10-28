using System.Text.Json;
using System.Text.Json.Nodes;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a dictionary's values are documented as legacy Blueprint documented them, in a response component and in
/// a request body: an open value as any value, a nullable value as nullable, and a read-only dictionary as a dictionary.
/// </summary>
/// <remarks>
/// Every expected <c>additionalProperties</c> here is legacy's, from NJsonSchema 11.1 run with legacy's settings (its
/// <c>BlueprintContractResolver</c>, <c>StringEnumConverter</c>, OpenAPI 3 and a flattened hierarchy) on the same shapes.
/// NJsonSchema documented an <c>object</c> value as any value (<c>{}</c>), took a value's nullability from its annotation,
/// and wrapped a nullable reference in <c>oneOf</c>. Signature365's <c>ImportedUser.Fields</c> is a
/// <c>Dictionary&lt;string, object?&gt;</c> and <c>VCardConfigurationApiResource.Fields</c> an
/// <c>IReadOnlyDictionary&lt;string, string?&gt;</c>; both were documented with no <c>additionalProperties</c> at all.
/// </remarks>
public sealed class OpenApiDictionaryValueTests
{
    [Theory]
    [InlineData("ObjectNullableValues", """{}""")]
    [InlineData("ObjectValues", """{}""")]
    [InlineData("ReadOnlyNullableStrings", """{ "type": "string", "nullable": true }""")]
    [InlineData("Strings", """{ "type": "string" }""")]
    [InlineData("NullableInts", """{ "type": "integer", "format": "int32", "nullable": true }""")]
    [InlineData("NullableChildren", """{ "nullable": true, "oneOf": [ { "$ref": "#/components/schemas/DictionaryShapeChild" } ] }""")]
    [InlineData("Children", """{ "$ref": "#/components/schemas/DictionaryShapeChild" }""")]
    [InlineData("StringArrays", """{ "type": "array", "items": { "type": "string" } }""")]
    [InlineData("NullableDictionary", """{ "type": "string" }""")]
    [InlineData("ObliviousStrings", """{ "type": "string" }""")]
    public void A_response_dictionary_documents_its_values_as_legacy_did(string property, string expectedAdditionalProperties)
    {
        using var document = Render();
        var schema = Component(document, "DictionaryShapeApiResource").GetProperty("properties").GetProperty(property);

        schema.GetProperty("type").GetString().Should().Be("object");
        AdditionalProperties(schema).Should().Be(Normalise(expectedAdditionalProperties));
    }

    [Theory]
    [InlineData("fields", """{ "type": "string", "nullable": true }""")]
    [InlineData("values", """{}""")]
    [InlineData("counts", """{ "type": "integer", "format": "int32", "nullable": true }""")]
    [InlineData("children", """{ "nullable": true, "oneOf": [ { "$ref": "#/components/schemas/DictionaryShapeChild" } ] }""")]
    [InlineData("labels", """{ "type": "string" }""")]
    public void A_request_body_dictionary_documents_its_values_as_legacy_did(string property, string expectedAdditionalProperties)
    {
        using var document = Render();
        var schema = Component(document, "UpdateDictionaryShape").GetProperty("properties").GetProperty(property);

        schema.GetProperty("type").GetString().Should().Be("object");
        AdditionalProperties(schema).Should().Be(Normalise(expectedAdditionalProperties));
    }

    private static JsonDocument Render()
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());

        return JsonDocument.Parse(rendered.Json);
    }

    private static JsonElement Component(JsonDocument document, string name)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(name);
    }

    /// <summary>The property's <c>additionalProperties</c> as compact JSON, or "(none)" when it has none.</summary>
    private static string AdditionalProperties(JsonElement schema)
    {
        return schema.TryGetProperty("additionalProperties", out var additionalProperties)
            ? Normalise(additionalProperties.GetRawText())
            : "(none)";
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

/// <summary>A resource with a dictionary of each value shape legacy told apart.</summary>
public sealed class DictionaryShapeApiResource
{
    // Schema-only: these values are never written, so only a registered primitive is declared (BP012).
    [BlueprintRuntimeTypes(typeof(string))]
    public Dictionary<string, object?> ObjectNullableValues { get; init; } = [];

    // Schema-only: these values are never written, so only a registered primitive is declared (BP012).
    [BlueprintRuntimeTypes(typeof(string))]
    public Dictionary<string, object> ObjectValues { get; init; } = [];

    public IReadOnlyDictionary<string, string?> ReadOnlyNullableStrings { get; init; } = new Dictionary<string, string?>();

    public Dictionary<string, string> Strings { get; init; } = [];

    public Dictionary<string, int?> NullableInts { get; init; } = [];

    public Dictionary<string, DictionaryShapeChild?> NullableChildren { get; init; } = [];

    public Dictionary<string, DictionaryShapeChild> Children { get; init; } = [];

    public IDictionary<string, string[]> StringArrays { get; init; } = new Dictionary<string, string[]>();

    // A nullable dictionary of values that are not: the dictionary's nullability is not its values'.
    public Dictionary<string, string>? NullableDictionary { get; init; }

    // Values with no annotation, which legacy did not document as nullable.
#nullable disable
    public Dictionary<string, string> ObliviousStrings { get; init; } = [];
#nullable restore
}

/// <summary>A value of the <see cref="DictionaryShapeApiResource"/>'s dictionaries.</summary>
public sealed class DictionaryShapeChild
{
    public string Name { get; init; } = string.Empty;
}

/// <summary>Gets the shape.</summary>
[AllowAnonymous]
[RootLink("/dictionary-shapes")]
public partial class GetDictionaryShapeQuery : IQuery<DictionaryShapeApiResource>
{
    public static DictionaryShapeApiResource Handle(GetDictionaryShapeQuery query)
    {
        return new DictionaryShapeApiResource();
    }
}

/// <summary>Updates the shape from a body with a dictionary of each value shape.</summary>
[AllowAnonymous]
[RootLink("/dictionary-shapes")]
[HttpPut]
public partial class UpdateDictionaryShapeCommand : ICommand<DictionaryShapeApiResource>
{
    public Dictionary<string, string?> Fields { get; set; } = [];

    public Dictionary<string, object?> Values { get; set; } = [];

    public IReadOnlyDictionary<string, int?> Counts { get; set; } = new Dictionary<string, int?>();

    public Dictionary<string, DictionaryShapeChild?> Children { get; set; } = [];

    public Dictionary<string, string>? Labels { get; set; }

    public static DictionaryShapeApiResource Handle(UpdateDictionaryShapeCommand command)
    {
        return new DictionaryShapeApiResource();
    }
}

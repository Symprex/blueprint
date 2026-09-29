using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a <see cref="RequiredAttribute"/> string is documented with <c>minLength: 1</c>, as legacy Blueprint's
/// NJsonSchema documented it, in a request body and a schema component, and not in a query parameter, where legacy did
/// not document it.
/// </summary>
/// <remarks>
/// <para>
/// Every expected value is what NJsonSchema.NewtonsoftJson 11.1.0 generates, with legacy's settings
/// (<c>Blueprint.OpenApi/OpenApiDocumentBuilder.cs:55-69</c> at Signature365 <c>8b40ecc961</c>), for a property of the same
/// shape (scratchpad <c>njs-minlength-probe</c>). NJsonSchema gives a <c>[Required]</c> property <c>minLength: 1</c> when
/// its JSON type is <c>string</c> and it is not an enum, unless <c>AllowEmptyStrings</c> is set, and a
/// <c>StringLength</c> or <c>MinLength</c> attribute on it replaces that with its own minimum. Signature365's legacy
/// documents carry it on every <c>[Required]</c> string of a command's body (Partners 33, Subscriptions 34), and on no
/// parameter, a <c>[Required]</c> query string among them.
/// </para>
/// <para>
/// Signature365's generated clients copy <c>minLength</c> into the validators their forms run
/// (<c>frontend/packages/openapi-generator/src/generator.mts:814</c>), so without it a form submits an empty required string.
/// </para>
/// </remarks>
public sealed class OpenApiRequiredStringMinLengthTests
{
    [Theory]
    [InlineData("name", 1)]
    [InlineData("code", 3)]
    [InlineData("initials", 2)]
    [InlineData("reference", 1)]
    public void A_request_body_documents_a_required_strings_minimum_length_as_legacy_did(string property, int expected)
    {
        using var document = Render(OpenApiRequiredProperties.NonNullable);

        MinLength(BodyProperty(document, property)).Should().Be(expected);
    }

    [Theory]
    [InlineData("blankable")]
    [InlineData("colour")]
    [InlineData("tags")]
    [InlineData("count")]
    [InlineData("note")]
    public void A_request_body_documents_no_minimum_length_where_legacy_documented_none(string property)
    {
        using var document = Render(OpenApiRequiredProperties.NonNullable);
        var schema = BodyProperty(document, property);

        MinLength(schema).Should().BeNull();
        schema.TryGetProperty("minItems", out _).Should().BeFalse("an array's length is written as minItems, which legacy did not give a [Required] list either");
    }

    [Fact]
    public void A_required_string_bounded_only_above_by_string_length_is_not_given_a_minimum_length()
    {
        // NJsonSchema gave it StringLength's minimum, 0, which constrains nothing; the framework documents a zero minimum
        // of StringLength as no minimum, as it does for a string that is not [Required].
        using var document = Render(OpenApiRequiredProperties.NonNullable);

        var label = BodyProperty(document, "label");

        (MinLength(label) ?? 0).Should().Be(0);
        label.GetProperty("maxLength").GetInt32().Should().Be(10);
    }

    [Fact]
    public void A_schema_component_documents_a_required_strings_minimum_length_as_legacy_did()
    {
        using var document = Render(OpenApiRequiredProperties.NonNullable);
        var properties = Component(document, "MinLengthShapeApiResource").GetProperty("properties");

        MinLength(properties.GetProperty("Name")).Should().Be(1);
        MinLength(properties.GetProperty("Note")).Should().BeNull();
    }

    [Fact]
    public void A_required_query_string_is_not_given_a_minimum_length()
    {
        using var document = Render(OpenApiRequiredProperties.NonNullable);

        var search = document.RootElement.GetProperty("paths").GetProperty("/min-length-shapes").GetProperty("get").GetProperty("parameters")
            .EnumerateArray()
            .Single(static parameter => parameter.GetProperty("name").GetString() == "search");

        search.GetProperty("required").GetBoolean().Should().BeTrue();
        MinLength(search.GetProperty("schema")).Should().BeNull();
    }

    [Fact]
    public void A_required_strings_minimum_length_is_documented_under_the_default_rule_too()
    {
        // It follows from [Required], which rejects an empty string whatever rule marks properties required, so it cannot
        // reject a value the server accepts.
        using var document = Render(OpenApiRequiredProperties.Declared);

        MinLength(BodyProperty(document, "name")).Should().Be(1);
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

    private static JsonElement BodyProperty(JsonDocument document, string name)
    {
        return Component(document, "CreateMinLengthShape").GetProperty("properties").GetProperty(name);
    }

    private static int? MinLength(JsonElement schema)
    {
        return schema.TryGetProperty("minLength", out var minLength) ? minLength.GetInt32() : null;
    }
}

/// <summary>A resource with a <see cref="RequiredAttribute"/> string.</summary>
public sealed class MinLengthShapeApiResource
{
    [Required]
    public string? Name { get; init; }

    public string? Note { get; init; }

    public MinLengthShapeToken? Token { get; init; }
}

/// <summary>A component nested in <see cref="MinLengthShapeApiResource"/>, bounded only above by <see cref="StringLengthAttribute"/>.</summary>
public sealed class MinLengthShapeToken
{
    [StringLength(50)]
    public string? Value { get; init; }

    public string? Unbounded { get; init; }
}

/// <summary>An enum of the <see cref="CreateMinLengthShapeCommand"/>.</summary>
public enum MinLengthShapeColour
{
    Red,
    Blue,
}

/// <summary>Gets the shape by a required query string.</summary>
[AllowAnonymous]
[RootLink("/min-length-shapes")]
public partial class GetMinLengthShapeQuery : IQuery<MinLengthShapeApiResource>
{
    [Required]
    public string? Search { get; set; }

    public static MinLengthShapeApiResource Handle(GetMinLengthShapeQuery query)
    {
        return new MinLengthShapeApiResource();
    }
}

/// <summary>Creates the shape from a body with a <see cref="RequiredAttribute"/> property of each shape NJsonSchema tells apart.</summary>
[AllowAnonymous]
[RootLink("/min-length-shapes")]
[HttpPost]
public partial class CreateMinLengthShapeCommand : ICommand<MinLengthShapeApiResource>
{
    [Required]
    public string? Name { get; set; }

    [Required(AllowEmptyStrings = true)]
    public string? Blankable { get; set; }

    [Required]
    [StringLength(10, MinimumLength = 3)]
    public string? Code { get; set; }

    [Required]
    [MinLength(2)]
    public string? Initials { get; set; }

    [Required]
    [StringLength(10)]
    public string? Label { get; set; }

    [Required]
    public MinLengthShapeColour? Colour { get; set; }

    [Required]
    public Guid? Reference { get; set; }

    [Required]
    public List<string>? Tags { get; set; }

    [Required]
    public int? Count { get; set; }

    public string? Note { get; set; }

    public static MinLengthShapeApiResource Handle(CreateMinLengthShapeCommand command)
    {
        return new MinLengthShapeApiResource();
    }
}

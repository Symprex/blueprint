using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.Validation;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves that under Legacy naming the bounds Blueprint's own comparison attributes (<c>[GreaterThan]</c>,
/// <c>[GreaterThanOrEqualTo]</c>, <c>[LessThan]</c>, <c>[LessThanOrEqualTo]</c>) declare are left out of a property's
/// schema, while a <c>[Range]</c>'s stay, and that Default naming documents all of them without an exclusive keyword: an
/// integer's exclusive bound as the next integer, a number's as the inclusive bound.
/// </summary>
/// <remarks>
/// Legacy's NJsonSchema described a property from the DataAnnotations attributes it knows, and Blueprint's comparison
/// attributes are plain <c>ValidationAttribute</c>s it does not: Signature365's twelve <c>[GreaterThan(0)]</c> properties
/// (<c>CreateSubscriptionCommand.Quantity</c> and eleven more) are <c>{"type", "format", "description"}</c> in every
/// committed document. Written as <c>"minimum": 0, "exclusiveMinimum": true</c>, they made the generated client's AJV
/// (which reads draft-07's numeric form) reject the whole schema and stop generating. Default cannot write either form
/// of an exclusive bound that both OpenAPI 3.0 (a boolean) and draft-07 (a number) accept, so it writes none: "greater than 0"
/// of an integer is exactly "at least 1", and of a number, "at least 0" never rejects a value the server accepts.
/// </remarks>
public sealed class OpenApiLegacyComparisonBoundTests
{
    [Fact]
    public void Under_legacy_naming_a_request_body_property_has_none_of_the_comparison_bounds()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        var properties = Properties(document, "SubmitComparisonBound");

        foreach (var name in new[] { "greater", "greaterorequal", "less", "lessorequal", "amount" })
        {
            NumericKeywords(properties[name]).Should().BeEmpty($"legacy ignored the comparison attribute on '{name}'");
        }
    }

    [Fact]
    public void Under_legacy_naming_a_request_body_range_keeps_its_bounds()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        var range = Properties(document, "SubmitComparisonBound")["range"];

        range.GetProperty("minimum").GetDecimal().Should().Be(1);
        range.GetProperty("maximum").GetDecimal().Should().Be(10);
    }

    [Fact]
    public void Under_legacy_naming_a_response_property_has_none_of_the_comparison_bounds_and_a_range_keeps_its_own()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        var properties = Properties(document, "ComparisonBoundApiResource");

        NumericKeywords(properties["greater"]).Should().BeEmpty("legacy ignored [GreaterThan]");
        NumericKeywords(properties["less"]).Should().BeEmpty("legacy ignored [LessThan]");
        properties["range"].GetProperty("minimum").GetDecimal().Should().Be(1);
        properties["range"].GetProperty("maximum").GetDecimal().Should().Be(10);
    }

    [Fact]
    public void Under_default_naming_a_request_body_property_documents_the_bounds_its_comparison_attribute_declares_without_an_exclusive_keyword()
    {
        using var document = Render(OpenApiSchemaNaming.Default);

        var properties = Properties(document, "SubmitComparisonBound");

        properties["greater"].GetProperty("minimum").GetDecimal().Should().Be(1, "an integer greater than 0 is at least 1");
        properties["greaterorequal"].GetProperty("minimum").GetDecimal().Should().Be(1);
        properties["less"].GetProperty("maximum").GetDecimal().Should().Be(99, "an integer less than 100 is at most 99");
        properties["lessorequal"].GetProperty("maximum").GetDecimal().Should().Be(9);
        properties["amount"].GetProperty("minimum").GetDecimal().Should().Be(0, "a number greater than 0 is written as at least 0, which rejects nothing the server accepts");

        foreach (var (name, property) in properties)
        {
            property.TryGetProperty("exclusiveMinimum", out _).Should().BeFalse($"'{name}': no draft-07 validator accepts OpenAPI 3.0's boolean");
            property.TryGetProperty("exclusiveMaximum", out _).Should().BeFalse($"'{name}': no OpenAPI 3.0 document accepts draft-07's number");
        }
    }

    [Fact]
    public void Under_default_naming_a_response_property_documents_the_bounds_its_comparison_attribute_declares_without_an_exclusive_keyword()
    {
        using var document = Render(OpenApiSchemaNaming.Default);

        var properties = Properties(document, "ComparisonBoundApiResource");

        properties["greater"].GetProperty("minimum").GetDecimal().Should().Be(1);
        properties["less"].GetProperty("maximum").GetDecimal().Should().Be(99);
        properties["amount"].GetProperty("minimum").GetDecimal().Should().Be(0);

        foreach (var (name, property) in properties)
        {
            property.TryGetProperty("exclusiveMinimum", out _).Should().BeFalse($"'{name}'");
            property.TryGetProperty("exclusiveMaximum", out _).Should().BeFalse($"'{name}'");
        }
    }

    [Fact]
    public void Under_default_naming_a_parameter_documents_no_exclusive_keyword_either()
    {
        using var document = Render(OpenApiSchemaNaming.Default);

        var schema = document.RootElement.GetProperty("paths").GetProperty("/legacy-shape/comparison-bound").GetProperty("get").GetProperty("parameters")
            .EnumerateArray()
            .Single(static parameter => string.Equals(parameter.GetProperty("name").GetString(), "ceiling", StringComparison.OrdinalIgnoreCase))
            .GetProperty("schema");

        schema.GetProperty("maximum").GetDecimal().Should().Be(99, "an integer less than 100 is at most 99");
        schema.TryGetProperty("exclusiveMaximum", out _).Should().BeFalse();
    }

    private static string[] NumericKeywords(JsonElement schema)
    {
        return schema.EnumerateObject()
            .Select(static property => property.Name)
            .Where(static name => name is "minimum" or "maximum" or "exclusiveMinimum" or "exclusiveMaximum")
            .ToArray();
    }

    private static Dictionary<string, JsonElement> Properties(JsonDocument document, string component)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(component).GetProperty("properties")
            .EnumerateObject()
            .ToDictionary(static property => property.Name.ToLowerInvariant(), static property => property.Value);
    }

    private static JsonDocument Render(OpenApiSchemaNaming naming)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(
            new IntegrationTestBlueprintApp(),
            new OpenApiInfoOptions { SchemaNaming = naming });

        return JsonDocument.Parse(rendered.Json);
    }
}

/// <summary>Bounds a response resource's properties by comparison attributes and a range.</summary>
public sealed class ComparisonBoundApiResource
{
    [GreaterThan(0)]
    public int Greater { get; set; }

    [LessThan(100)]
    public int Less { get; set; }

    [Range(1, 10)]
    public int Range { get; set; }

    [GreaterThan(0)]
    public decimal Amount { get; set; }
}

/// <summary>Reads the resource whose properties are bounded.</summary>
[AllowAnonymous]
[RootLink("/legacy-shape/comparison-bound")]
public partial class GetComparisonBoundQuery : IQuery<ComparisonBoundApiResource>
{
    [LessThan(100)]
    public int Ceiling { get; set; }

    public static ComparisonBoundApiResource Handle(GetComparisonBoundQuery query)
    {
        return new ComparisonBoundApiResource();
    }
}

/// <summary>Bounds a request body's properties by each comparison attribute and a range.</summary>
[AllowAnonymous]
[RootLink("/legacy-shape/comparison-bound")]
[HttpPost]
public partial class SubmitComparisonBoundCommand : ICommand<string>
{
    [GreaterThan(0)]
    public int Greater { get; set; }

    [GreaterThanOrEqualTo(1)]
    public int GreaterOrEqual { get; set; }

    [LessThan(100)]
    public int Less { get; set; }

    [LessThanOrEqualTo(9)]
    public int LessOrEqual { get; set; }

    [Range(1, 10)]
    public int Range { get; set; }

    [GreaterThan(0)]
    public decimal Amount { get; set; }

    public static string Handle(SubmitComparisonBoundCommand command)
    {
        return command.Greater.ToString();
    }
}

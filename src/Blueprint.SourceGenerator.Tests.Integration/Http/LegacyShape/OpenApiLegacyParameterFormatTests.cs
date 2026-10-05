using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves that under Legacy naming a parameter's <c>format</c> is only its type's (<c>int32</c>, <c>int64</c>,
/// <c>date-time</c>), never one a validation attribute implies (<c>[EmailAddress]</c> gives <c>email</c>), and that
/// Default naming keeps the validation-derived format.
/// </summary>
/// <remarks>
/// Legacy generated a parameter's schema by reflection on its type alone, so none of the 811 parameters in Signature365's
/// 8b40ecc961 committed documents has a format but <c>int32</c> (95), <c>int64</c> (5) or <c>date-time</c> (10); a string
/// parameter has none, whatever attribute it carries. The request-body property of the same attribute keeps its format.
/// </remarks>
public sealed class OpenApiLegacyParameterFormatTests
{
    [Fact]
    public void Under_legacy_naming_a_query_parameter_has_only_its_types_format()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        Schema(document, "sender").TryGetProperty("format", out _).Should().BeFalse("legacy never derived a parameter's format from [EmailAddress]");
        Schema(document, "from").GetProperty("format").GetString().Should().Be("date-time");
    }

    [Fact]
    public void Under_default_naming_a_query_parameter_keeps_the_format_its_validation_attribute_implies()
    {
        using var document = Render(OpenApiSchemaNaming.Default);

        Schema(document, "sender").GetProperty("format").GetString().Should().Be("email");
        Schema(document, "from").GetProperty("format").GetString().Should().Be("date-time");
    }

    [Fact]
    public void Under_legacy_naming_a_request_body_property_keeps_the_format_its_validation_attribute_implies()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        var contact = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("SubmitParameterFormat")
            .GetProperty("properties").GetProperty("contact");
        contact.GetProperty("format").GetString().Should().Be("email");
    }

    private static JsonDocument Render(OpenApiSchemaNaming naming)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(
            new IntegrationTestBlueprintApp(),
            new OpenApiInfoOptions { SchemaNaming = naming });

        return JsonDocument.Parse(rendered.Json);
    }

    private static JsonElement Schema(JsonDocument document, string parameterName)
    {
        return document.RootElement.GetProperty("paths").GetProperty("/legacy-shape/parameter-format").GetProperty("get").GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter => string.Equals(parameter.GetProperty("name").GetString(), parameterName, StringComparison.OrdinalIgnoreCase))
            .GetProperty("schema");
    }
}

/// <summary>Reads with an <c>[EmailAddress]</c> string and a date query parameter.</summary>
[AllowAnonymous]
[RootLink("/legacy-shape/parameter-format")]
public partial class GetParameterFormatQuery : IQuery<string>
{
    [EmailAddress]
    public string? Sender { get; set; }

    public DateTimeOffset? From { get; set; }

    public static string Handle(GetParameterFormatQuery query)
    {
        return query.Sender ?? string.Empty;
    }
}

/// <summary>Submits an <c>[EmailAddress]</c> string in a body.</summary>
[AllowAnonymous]
[RootLink("/legacy-shape/parameter-format")]
[HttpPost]
public partial class SubmitParameterFormatCommand : ICommand<string>
{
    [EmailAddress]
    public string? Contact { get; set; }

    public static string Handle(SubmitParameterFormatCommand command)
    {
        return command.Contact ?? string.Empty;
    }
}

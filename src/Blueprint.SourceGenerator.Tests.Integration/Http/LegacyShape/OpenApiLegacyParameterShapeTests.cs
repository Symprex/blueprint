using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves that under Legacy naming a parameter object has the shape legacy wrote: no <c>required: false</c>, no
/// <c>nullable</c> on its schema, no <c>style</c> or <c>explode</c>, no <c>x-enum-varnames</c> on an enum, and an enum's
/// description on its schema, and that a query parameter with an explicit binding alias is named by the alias when
/// parameters are named by their property; Default naming keeps what it has always written, an alias's property name
/// included.
/// </summary>
/// <remarks>
/// Signature365's 734 legacy parameters carry no <c>nullable</c> and no <c>required: false</c> (550 are <c>required:
/// true</c>, 184 have no <c>required</c>), and the only schema keywords are <c>type</c>, <c>format</c>, <c>title</c>,
/// <c>enum</c> (with <c>x-enumNames</c>) and, for an enum, its summary as <c>description</c> (Scope, SearchType, SurveyType
/// and ImportItemType), which the generated client reads. Microsoft365.Gateway's <c>[FromQuery("validationToken")]</c>
/// was named by the alias, legacy having named a parameter by the name its owning descriptor gave it.
/// </remarks>
public sealed class OpenApiLegacyParameterShapeTests
{
    [Fact]
    public void Under_legacy_naming_an_optional_parameter_has_no_required_flag_and_its_schema_no_nullable()
    {
        using var document = Render(new OpenApiInfoOptions { SchemaNaming = OpenApiSchemaNaming.Legacy, QueryParameterNaming = OpenApiParameterNaming.PropertyName });

        var optional = Parameter(document, "Optional");
        optional.TryGetProperty("required", out _).Should().BeFalse("legacy never wrote required: false");
        optional.GetProperty("schema").TryGetProperty("nullable", out _).Should().BeFalse("legacy never wrote nullable on a parameter");

        Parameter(document, "Needed").GetProperty("required").GetBoolean().Should().BeTrue();
        Parameter(document, "Id").GetProperty("required").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Under_legacy_naming_an_array_parameter_has_no_style_or_explode()
    {
        using var document = Render(new OpenApiInfoOptions { SchemaNaming = OpenApiSchemaNaming.Legacy, QueryParameterNaming = OpenApiParameterNaming.PropertyName });

        var tags = Parameter(document, "Tags");
        tags.TryGetProperty("style", out _).Should().BeFalse();
        tags.TryGetProperty("explode", out _).Should().BeFalse();
        tags.GetProperty("schema").GetProperty("type").GetString().Should().Be("array");
    }

    [Fact]
    public void Under_legacy_naming_an_enum_parameters_schema_has_its_description_and_x_enumNames_but_no_x_enum_varnames()
    {
        using var document = Render(new OpenApiInfoOptions { SchemaNaming = OpenApiSchemaNaming.Legacy, QueryParameterNaming = OpenApiParameterNaming.PropertyName });

        var schema = Parameter(document, "Scope").GetProperty("schema");
        schema.GetProperty("description").GetString().Should().Be("Where a parameter shape applies.");
        schema.GetProperty("enum").EnumerateArray().Select(static value => value.GetString()).Should().Equal("Local", "Global");
        schema.GetProperty("x-enumNames").EnumerateArray().Select(static value => value.GetString()).Should().Equal("Local", "Global");
        schema.TryGetProperty("x-enum-varnames", out _).Should().BeFalse("legacy wrote x-enumNames alone");
    }

    [Fact]
    public void Under_legacy_naming_a_query_parameter_with_an_alias_is_named_by_the_alias()
    {
        using var document = Render(new OpenApiInfoOptions { SchemaNaming = OpenApiSchemaNaming.Legacy, QueryParameterNaming = OpenApiParameterNaming.PropertyName });

        var names = ParameterNames(document);

        names.Should().Contain("validationToken", "the alias is the name the request binds by");
        names.Should().NotContain("Token");
        names.Should().Contain("PlainName", "a binding name that differs only by case is the property's own name");
    }

    [Fact]
    public void When_parameters_are_named_by_their_property_without_legacy_naming_an_alias_is_still_the_property_name()
    {
        using var document = Render(new OpenApiInfoOptions { QueryParameterNaming = OpenApiParameterNaming.PropertyName });

        var names = ParameterNames(document);

        names.Should().Contain("Token", "only Legacy naming names an aliased parameter by its alias");
        names.Should().NotContain("validationToken");
        names.Should().Contain("PlainName");
    }

    [Fact]
    public void Under_default_naming_a_parameter_keeps_its_required_flag_nullable_style_and_varnames()
    {
        using var document = Render(new OpenApiInfoOptions());

        var optional = Parameter(document, "optional");
        optional.GetProperty("required").GetBoolean().Should().BeFalse();
        optional.GetProperty("schema").GetProperty("nullable").GetBoolean().Should().BeTrue();

        var tags = Parameter(document, "tags");
        tags.GetProperty("style").GetString().Should().Be("form");
        tags.GetProperty("explode").GetBoolean().Should().BeTrue();

        var scope = Parameter(document, "scope").GetProperty("schema");
        scope.GetProperty("x-enum-varnames").EnumerateArray().Select(static value => value.GetString()).Should().Equal("Local", "Global");
        scope.TryGetProperty("description", out _).Should().BeFalse();

        ParameterNames(document).Should().Contain("validationToken").And.Contain("plainName");
    }

    private static List<string> ParameterNames(JsonDocument document)
    {
        return Parameters(document).Select(static parameter => parameter.GetProperty("name").GetString()!).ToList();
    }

    private static JsonElement Parameter(JsonDocument document, string name)
    {
        return Parameters(document).Single(parameter => string.Equals(parameter.GetProperty("name").GetString(), name, StringComparison.Ordinal));
    }

    private static IEnumerable<JsonElement> Parameters(JsonDocument document)
    {
        return document.RootElement.GetProperty("paths").GetProperty("/legacy-shape/parameter-shape/{Id}").GetProperty("get").GetProperty("parameters").EnumerateArray();
    }

    private static JsonDocument Render(OpenApiInfoOptions options)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), options);

        return JsonDocument.Parse(rendered.Json);
    }
}

/// <summary>Where a parameter shape applies.</summary>
public enum ParameterShapeScope
{
    Local,
    Global,
}

/// <summary>Reads with a route, required and optional query parameters, an alias, an array and an enum.</summary>
[AllowAnonymous]
[RootLink("/legacy-shape/parameter-shape/{Id}")]
public partial class GetParameterShapeQuery : IQuery<string>
{
    public string Id { get; set; } = string.Empty;

    [Required]
    public string Needed { get; set; } = string.Empty;

    public int? Optional { get; set; }

    public string[]? Tags { get; set; }

    public ParameterShapeScope? Scope { get; set; }

    [FromQuery("validationToken")]
    public string? Token { get; set; }

    public string? PlainName { get; set; }

    public static string Handle(GetParameterShapeQuery query)
    {
        return query.Id;
    }
}

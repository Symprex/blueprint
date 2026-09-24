using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that, under <see cref="OpenApiSchemaNaming.Legacy"/>, a parameter's schema carries the title legacy Blueprint
/// gave it — a typed id's included — and that the default document is left as it was.
/// </summary>
/// <remarks>
/// Legacy's <c>OpenApiDocumentBuilder</c> gave each parameter <c>generator.Generate(property.PropertyType)</c>, a root
/// schema, which NJsonSchema titles with its schema name generator: legacy's <c>BlueprintSchemaNameGenerator</c>, NJsonSchema's
/// <c>DefaultSchemaNameGenerator</c> with <c>"ApiResource"</c> and <c>"PagedOf"</c> replaced. It names the type a
/// <see cref="Nullable{T}"/> wraps. Every expected title here is legacy's, from NJsonSchema 11.1 run with legacy's settings
/// on the same types; Signature365's committed documents title its <c>ApiId</c> parameters <c>"ApiId"</c>.
/// </remarks>
public sealed class OpenApiParameterTitleTests
{
    [Theory]
    [InlineData("Id", "TypedIdFixture")]
    [InlineData("Other", "TypedIdFixture")]
    [InlineData("Name", "String")]
    [InlineData("Page", "Integer")]
    [InlineData("Size", "Integer")]
    [InlineData("Total", "Long")]
    [InlineData("Flag", "Boolean")]
    [InlineData("Key", "Guid")]
    [InlineData("Colour", "ParameterTitleColour")]
    [InlineData("Colours", "ParameterTitleColour[]")]
    [InlineData("Codes", "String[]")]
    public void Under_legacy_naming_a_parameter_schema_has_legacy_title(string parameter, string expectedTitle)
    {
        using var document = Render(new OpenApiInfoOptions { SchemaNaming = OpenApiSchemaNaming.Legacy });

        Schema(document, parameter).GetProperty("title").GetString().Should().Be(expectedTitle);
    }

    [Fact]
    public void Under_legacy_naming_a_parameter_title_is_formatted_as_the_schema_names_are()
    {
        using var document = Render(new OpenApiInfoOptions
        {
            SchemaNaming = OpenApiSchemaNaming.Legacy,
            SchemaNameFormatter = static name => name.Replace("Colour", "Color", StringComparison.Ordinal),
        });

        Schema(document, "Colour").GetProperty("title").GetString().Should().Be("ParameterTitleColor");
        Schema(document, "Colours").GetProperty("title").GetString().Should().Be("ParameterTitleColor[]");
        Schema(document, "Name").GetProperty("title").GetString().Should().Be("String");
    }

    [Fact]
    public void By_default_a_parameter_schema_has_no_title()
    {
        using var document = Render(new OpenApiInfoOptions());

        Parameters(document).Select(static parameter => parameter.GetProperty("schema").TryGetProperty("title", out var title) ? title.GetString() : null)
            .Should().OnlyContain(static title => title == null);
    }

    private static JsonDocument Render(OpenApiInfoOptions options)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), options);

        return JsonDocument.Parse(rendered.Json);
    }

    private static IEnumerable<JsonElement> Parameters(JsonDocument document)
    {
        return document.RootElement.GetProperty("paths").GetProperty("/parameter-titles/{Id}").GetProperty("get").GetProperty("parameters")
            .EnumerateArray()
            .ToList();
    }

    /// <summary>The schema of the named parameter, whatever the parameter naming's casing.</summary>
    private static JsonElement Schema(JsonDocument document, string parameter)
    {
        return Parameters(document)
            .Single(p => string.Equals(p.GetProperty("name").GetString(), parameter, StringComparison.OrdinalIgnoreCase))
            .GetProperty("schema");
    }
}

/// <summary>The resource the parameter title query returns.</summary>
public sealed class ParameterTitlesApiResource
{
    public string Id { get; init; } = string.Empty;
}

/// <summary>An enum parameter of the parameter title query.</summary>
public enum ParameterTitleColour
{
    Red,
    Blue,
}

/// <summary>Gets a resource by a typed id, filtered by a query parameter of each type legacy titled apart.</summary>
[AllowAnonymous]
[RootLink("/parameter-titles/{Id}")]
public partial class GetParameterTitlesQuery : IQuery<ParameterTitlesApiResource>
{
    public TypedIdFixture Id { get; set; }

    public TypedIdFixture? Other { get; set; }

    public string? Name { get; set; }

    public int Page { get; set; }

    public int? Size { get; set; }

    public long Total { get; set; }

    public bool Flag { get; set; }

    public Guid Key { get; set; }

    public ParameterTitleColour Colour { get; set; }

    public ParameterTitleColour[]? Colours { get; set; }

    public string[]? Codes { get; set; }

    public static ParameterTitlesApiResource Handle(GetParameterTitlesQuery query)
    {
        return new ParameterTitlesApiResource();
    }
}

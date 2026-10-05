using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that <see cref="OpenApiRequiredProperties.NonNullable"/> marks required what legacy Blueprint marked required,
/// in a response component, a request body and a query, and that the default leaves the document as it was.
/// </summary>
/// <remarks>
/// <para>
/// Legacy's <c>BlueprintContractResolver.CreateProperty</c> made every property whose type is not nullable
/// <c>Required.Always</c> (Namotion's <c>Nullability.NotNullable</c>: a value type that is not <c>Nullable&lt;T&gt;</c>, and
/// a reference type annotated as not nullable), and NJsonSchema added every <c>[Required]</c> one, which it then did not
/// document as nullable. A parameter was required when it was a route parameter, not nullable, or <c>[Required]</c>.
/// </para>
/// <para>
/// Every expected list here is legacy's, from its NJsonSchema 11.1 settings and that resolver run on the same shapes.
/// </para>
/// </remarks>
public sealed class OpenApiRequiredPropertiesTests
{
    [Fact]
    public void Under_the_legacy_rule_every_property_whose_type_is_not_nullable_is_required()
    {
        using var document = Render(OpenApiRequiredProperties.NonNullable);

        Required(Component(document, "RequiredShapeApiResource")).Should().BeEquivalentTo(
            "NonNullString", "Int", "Flag", "NonNullChild", "NonNullList", "Colour", "RequiredNullableString", "Extensions", "When");
    }

    [Fact]
    public void Under_the_legacy_rule_a_required_property_whose_type_is_nullable_is_not_documented_as_nullable()
    {
        using var document = Render(OpenApiRequiredProperties.NonNullable);
        var properties = Component(document, "RequiredShapeApiResource").GetProperty("properties");

        properties.GetProperty("RequiredNullableString").TryGetProperty("nullable", out _).Should().BeFalse();
        properties.GetProperty("NullableString").GetProperty("nullable").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Under_the_legacy_rule_the_required_keyword_does_not_make_a_nullable_property_required()
    {
        // Legacy's Newtonsoft.Json contract knew nothing of the C# required keyword: NJsonSchema 11.1 with legacy's settings
        // leaves a `required string?` property out of `required` and documents it nullable (scratchpad njs-minlength-probe).
        // So Signature365's `public required IEnumerable<Expandable<AzureAdGroupApiResource>>? LimitToGroups`
        // (Microsoft365.Contract/Api/Resources/ImportMicrosoft365Capability.cs:37) is missing from legacy Portal's
        // ImportMicrosoft365Capability.required, and documented nullable (Portal/Spec/openapi.json at 8b40ecc961).
        using var document = Render(OpenApiRequiredProperties.NonNullable);
        var component = Component(document, "RequiredShapeApiResource");

        Required(component).Should().NotContain("RequiredKeywordNullableString");
        component.GetProperty("properties").GetProperty("RequiredKeywordNullableString").GetProperty("nullable").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Under_the_legacy_rule_a_request_body_requires_its_non_nullable_properties()
    {
        using var document = Render(OpenApiRequiredProperties.NonNullable);
        var body = Component(document, "CreateRequiredShape");

        Required(body).Should().BeEquivalentTo("title", "code");
        body.GetProperty("properties").GetProperty("code").TryGetProperty("nullable", out _).Should().BeFalse();
        body.GetProperty("properties").GetProperty("note").GetProperty("nullable").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Under_the_legacy_rule_a_query_parameter_whose_type_is_not_nullable_is_required()
    {
        using var document = Render(OpenApiRequiredProperties.NonNullable);

        RequiredParameters(document).Should().BeEquivalentTo("flag", "code");
    }

    [Fact]
    public void By_default_only_what_is_declared_required_is_required()
    {
        using var document = Render(OpenApiRequiredProperties.Declared);

        // A required member is one System.Text.Json requires, so the default rule, which documents what is declared, does.
        Required(Component(document, "RequiredShapeApiResource")).Should().Equal("RequiredKeywordNullableString");
        Required(Component(document, "CreateRequiredShape")).Should().BeEquivalentTo("code");
        RequiredParameters(document).Should().BeEquivalentTo("code");
        Component(document, "CreateRequiredShape").GetProperty("properties").GetProperty("code").GetProperty("nullable").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Under_the_legacy_rule_a_problem_still_requires_only_its_status()
    {
        using var document = Render(OpenApiRequiredProperties.NonNullable);

        Required(Component(document, "ProblemDetails")).Should().Equal("status");
        Required(Component(document, "ValidationProblemDetails")).Should().Equal("status");
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

    private static IReadOnlyList<string> Required(JsonElement component)
    {
        return component.TryGetProperty("required", out var required)
            ? required.EnumerateArray().Select(static name => name.GetString()!).ToList()
            : [];
    }

    private static IReadOnlyList<string> RequiredParameters(JsonDocument document)
    {
        return document.RootElement.GetProperty("paths").GetProperty("/required-shapes").GetProperty("get").GetProperty("parameters")
            .EnumerateArray()
            .Where(static parameter => parameter.GetProperty("required").GetBoolean())
            .Select(static parameter => parameter.GetProperty("name").GetString()!)
            .ToList();
    }
}

/// <summary>A resource with a property of each shape legacy's required rule tells apart.</summary>
public sealed class RequiredShapeApiResource
{
    public string NonNullString { get; init; } = string.Empty;

    public string? NullableString { get; init; }

    public int Int { get; init; }

    public int? NullableInt { get; init; }

    public bool Flag { get; init; }

    public RequiredShapeChild NonNullChild { get; init; } = new();

    public RequiredShapeChild? NullableChild { get; init; }

    public List<string> NonNullList { get; init; } = [];

    public List<string>? NullableList { get; init; }

    public RequiredShapeColour Colour { get; init; }

    public RequiredShapeColour? NullableColour { get; init; }

    [Required]
    public string? RequiredNullableString { get; init; }

    public Dictionary<string, object?> Extensions { get; init; } = [];

    public DateTimeOffset When { get; init; }

    // The C# required keyword, which System.Text.Json reads as required and legacy's Newtonsoft.Json contract ignored.
    public required string? RequiredKeywordNullableString { get; init; }

    // A reference type with no annotation, which legacy did not count as either nullable or not.
#nullable disable
    public string ObliviousString { get; init; }
#nullable restore
}

/// <summary>A nested object of the <see cref="RequiredShapeApiResource"/>.</summary>
public sealed class RequiredShapeChild
{
    public string? Name { get; init; }
}

/// <summary>An enum of the <see cref="RequiredShapeApiResource"/>.</summary>
public enum RequiredShapeColour
{
    Red,
    Blue,
}

/// <summary>Gets the shape, filtered by query parameters of each shape.</summary>
[AllowAnonymous]
[RootLink("/required-shapes")]
public partial class GetRequiredShapeQuery : IQuery<RequiredShapeApiResource>
{
    public bool Flag { get; set; }

    public string? Name { get; set; }

    public int? Page { get; set; }

    [Required]
    public string? Code { get; set; }

    public static RequiredShapeApiResource Handle(GetRequiredShapeQuery query)
    {
        return new RequiredShapeApiResource { RequiredKeywordNullableString = null };
    }
}

/// <summary>Creates the shape from a body with a property of each shape.</summary>
[AllowAnonymous]
[RootLink("/required-shapes")]
[HttpPost]
public partial class CreateRequiredShapeCommand : ICommand<RequiredShapeApiResource>
{
    public string Title { get; set; } = string.Empty;

    public string? Note { get; set; }

    [Required]
    public string? Code { get; set; }

    public static RequiredShapeApiResource Handle(CreateRequiredShapeCommand command)
    {
        return new RequiredShapeApiResource { RequiredKeywordNullableString = null };
    }
}

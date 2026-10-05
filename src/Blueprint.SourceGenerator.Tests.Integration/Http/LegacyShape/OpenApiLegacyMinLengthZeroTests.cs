using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves that a request-body property bounded only by <c>[StringLength(max)]</c> (no explicit minimum, no
/// <c>[Required]</c>) carries legacy's own <c>"minLength": 0</c> under Legacy naming, and carries no minLength at all
/// under Default naming.
/// </summary>
/// <remarks>
/// Legacy's NJsonSchema echoed a <see cref="StringLengthAttribute"/>'s <see cref="StringLengthAttribute.MinimumLength"/>
/// verbatim, including its default of 0. Signature365's committed Portal document proves it: 21 properties across the
/// document carry <c>"minLength": 0</c> (e.g. a signature's optional description, bounded only by
/// <c>[StringLength(200)]</c>) — a fact that constrains nothing, which this document otherwise correctly leaves out.
/// </remarks>
public sealed class OpenApiLegacyMinLengthZeroTests
{
    [Fact]
    public void Under_legacy_naming_a_string_length_max_only_property_carries_minLength_zero()
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(
            new IntegrationTestBlueprintApp(),
            new OpenApiInfoOptions { SchemaNaming = OpenApiSchemaNaming.Legacy });
        using var document = JsonDocument.Parse(rendered.Json);

        var description = ComponentProperty(document, "SubmitBoundedDescription", "description");
        description.GetProperty("minLength").GetInt32().Should().Be(0);
        description.GetProperty("maxLength").GetInt32().Should().Be(200);
    }

    [Fact]
    public void Under_default_naming_a_string_length_max_only_property_carries_no_minLength()
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());
        using var document = JsonDocument.Parse(rendered.Json);

        var description = ComponentProperty(document, "SubmitBoundedDescription", "description");
        description.TryGetProperty("minLength", out _).Should().BeFalse(
            "a minimum of 0 constrains nothing, which the framework's own text correctly leaves out");
        description.GetProperty("maxLength").GetInt32().Should().Be(200);
    }

    [Fact]
    public void Under_legacy_naming_a_nested_components_string_length_max_only_property_carries_minLength_zero()
    {
        // Legacy wrote it on nested components too: Signature365's Portal TenantCustomFieldValue.token, .displayName and
        // .value, and EditableUserFieldValue.token, are bounded only above.
        var (_, rendered) = OpenApiDocumentEndpoint.Create(
            new IntegrationTestBlueprintApp(),
            new OpenApiInfoOptions { SchemaNaming = OpenApiSchemaNaming.Legacy });
        using var document = JsonDocument.Parse(rendered.Json);

        var value = ComponentProperty(document, "MinLengthShapeToken", "Value");
        value.GetProperty("minLength").GetInt32().Should().Be(0);
        value.GetProperty("maxLength").GetInt32().Should().Be(50);

        ComponentProperty(document, "MinLengthShapeToken", "Unbounded").TryGetProperty("minLength", out _).Should().BeFalse(
            "a string with no [StringLength] declares no minimum, not even 0");
    }

    [Fact]
    public void Under_default_naming_a_nested_components_string_length_max_only_property_carries_no_minLength()
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());
        using var document = JsonDocument.Parse(rendered.Json);

        var value = ComponentProperty(document, "MinLengthShapeToken", "Value");
        value.TryGetProperty("minLength", out _).Should().BeFalse();
        value.GetProperty("maxLength").GetInt32().Should().Be(50);
    }

    private static JsonElement ComponentProperty(JsonDocument document, string componentName, string propertyName)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(componentName)
            .GetProperty("properties").GetProperty(propertyName);
    }
}

/// <summary>Submits a description bounded only by <c>[StringLength(max)]</c>, with no explicit minimum or [Required].</summary>
[AllowAnonymous]
[RootLink("/legacy-shape/bounded-description")]
[HttpPost]
public partial class SubmitBoundedDescriptionCommand : ICommand<string>
{
    [StringLength(200)]
    public string? Description { get; set; }

    public static string Handle(SubmitBoundedDescriptionCommand command)
    {
        return command.Description ?? string.Empty;
    }
}

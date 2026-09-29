using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves how a deprecated property is written: <c>deprecated</c> on every form a property takes, including a reference to
/// a component, in both namings; the message under Legacy naming as <c>x-deprecatedMessage</c>, as legacy's NJsonSchema
/// wrote it, and as <c>x-deprecated-reason</c> under Default; and an enum without <c>x-enum-varnames</c> under Legacy.
/// </summary>
/// <remarks>
/// <para>
/// Signature365's 18 legacy documents carry 56 <c>deprecated: true</c>, every one on a component property (41 plain, 13 a
/// <c>oneOf</c> reference beside <c>nullable</c> and <c>description</c>, such as Outlook's
/// <c>AgentConfiguration.newMailFont</c>), 54 with an <c>x-deprecatedMessage</c> and 2 without; none on an operation or a
/// parameter. They carry <c>x-enumNames</c> on all 203 enums and never <c>x-enum-varnames</c>. Generators for the
/// document read neither, but the 13 references lost <c>deprecated</c> altogether.
/// </para>
/// <para>
/// The <c>oneOf</c> form dropping <c>deprecated</c> is a defect in every mode, so it is fixed in both; the key the
/// message is written under, and the extra enum extension, are legacy's own and so follow Legacy naming.
/// </para>
/// </remarks>
public sealed class OpenApiLegacyDeprecationTests
{
    [Theory]
    [InlineData(OpenApiSchemaNaming.Default, "oldTier")]
    [InlineData(OpenApiSchemaNaming.Legacy, "oldTier")]
    [InlineData(OpenApiSchemaNaming.Default, "oldAddress")]
    [InlineData(OpenApiSchemaNaming.Legacy, "oldAddress")]
    [InlineData(OpenApiSchemaNaming.Default, "oldRequiredAddress")]
    [InlineData(OpenApiSchemaNaming.Legacy, "oldRequiredAddress")]
    [InlineData(OpenApiSchemaNaming.Default, "oldName")]
    [InlineData(OpenApiSchemaNaming.Legacy, "oldName")]

    public void A_deprecated_property_is_marked_deprecated_whatever_form_it_takes(OpenApiSchemaNaming naming, string property)
    {
        using var document = Render(naming);

        Property(document, property).GetProperty("deprecated").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData("oldTier")]
    [InlineData("oldAddress")]
    [InlineData("oldRequiredAddress")]
    public void A_deprecated_property_that_references_a_component_still_references_it(string property)
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        Property(document, property).GetProperty("oneOf").EnumerateArray().Single().GetProperty("$ref").GetString().Should().StartWith("#/components/schemas/");
    }

    [Theory]
    [InlineData("oldTier", "Use Tier instead.")]
    [InlineData("oldAddress", "Use Address instead.")]
    [InlineData("oldName", "Use Name instead.")]
    public void Under_legacy_naming_the_deprecation_message_is_x_deprecatedMessage(string property, string message)
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);
        var schema = Property(document, property);

        schema.GetProperty("x-deprecatedMessage").GetString().Should().Be(message);
        schema.TryGetProperty("x-deprecated-reason", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("oldTier", "Use Tier instead.")]
    [InlineData("oldAddress", "Use Address instead.")]
    [InlineData("oldName", "Use Name instead.")]
    public void Under_default_naming_the_deprecation_message_is_x_deprecated_reason(string property, string message)
    {
        using var document = Render(OpenApiSchemaNaming.Default);
        var schema = Property(document, property);

        schema.GetProperty("x-deprecated-reason").GetString().Should().Be(message);
        schema.TryGetProperty("x-deprecatedMessage", out _).Should().BeFalse();
    }

    [Fact]
    public void Under_legacy_naming_an_enum_has_x_enumNames_and_no_x_enum_varnames()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);
        var tier = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("LegacyShapeTier");

        tier.GetProperty("x-enumNames").EnumerateArray().Select(static e => e.GetString()).Should().Equal("Basic", "Pro");
        tier.TryGetProperty("x-enum-varnames", out _).Should().BeFalse();
    }

    [Fact]
    public void Under_default_naming_an_enum_has_both_extensions()
    {
        using var document = Render(OpenApiSchemaNaming.Default);
        var tier = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("LegacyShapeTier");

        tier.GetProperty("x-enumNames").EnumerateArray().Select(static e => e.GetString()).Should().Equal("Basic", "Pro");
        tier.GetProperty("x-enum-varnames").EnumerateArray().Select(static e => e.GetString()).Should().Equal("Basic", "Pro");
    }

    private static JsonElement Property(JsonDocument document, string property)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("CreateDeprecationShape")
            .GetProperty("properties").GetProperty(property);
    }

    private static JsonDocument Render(OpenApiSchemaNaming naming)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(
            new IntegrationTestBlueprintApp(),
            new OpenApiInfoOptions { SchemaNaming = naming });

        return JsonDocument.Parse(rendered.Json);
    }
}

/// <summary>A command whose body has a deprecated property of each form: an enum, a component and a scalar.</summary>
[AllowAnonymous]
[RootLink("/deprecation-shapes")]
[HttpPost]
public partial class CreateDeprecationShapeCommand : ICommand<string>
{
    [Obsolete("Use Tier instead.")]
    public LegacyShapeTierKind OldTier { get; set; }

    [Obsolete("Use Address instead.")]
    public LegacyShapeAddress? OldAddress { get; set; }

    [Obsolete("Use Name instead.")]
    public string? OldName { get; set; }

    [Obsolete("Use Address instead.")]
    public LegacyShapeAddress OldRequiredAddress { get; set; } = new();

    public static string Handle(CreateDeprecationShapeCommand command)
    {
        return "created";
    }
}

using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves that under Legacy naming a schema property's validation-derived <c>format</c> is only one legacy's NJsonSchema
/// wrote: <c>email</c>, <c>phone</c> or <c>uri</c> from an attribute that is a <see cref="DataTypeAttribute"/>, and none
/// from Blueprint's own <c>[EmailAddress]</c>/<c>[Url]</c> or from <c>[CreditCard]</c>. Default naming keeps them all.
/// </summary>
/// <remarks>
/// NJsonSchema read a format from a property's <see cref="DataTypeAttribute"/> through its DataType map (EmailAddress,
/// PhoneNumber, Url and the date types); <c>CreditCard</c> is not in it. Blueprint's attributes are
/// <c>RegexAttribute</c>s, so it never saw them: Signature365's committed Portal document has no format on
/// <c>PortalInviteTenantUserCommand.emailAddress</c> or the tenant social-profile URLs, while its
/// <c>[System.ComponentModel.DataAnnotations.EmailAddress]</c> properties carry <c>email</c>. A generated client turns a
/// format into a validator that rejects input, so a format legacy never wrote changes what its forms accept.
/// </remarks>
public sealed class OpenApiLegacyBodyFormatTests
{
    [Fact]
    public void Under_legacy_naming_a_body_property_has_no_format_from_blueprints_attributes_or_credit_card()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        var properties = Properties(document, "SubmitBodyFormat");

        properties.GetProperty("blueprintEmail").TryGetProperty("format", out _).Should().BeFalse();
        properties.GetProperty("blueprintUrl").TryGetProperty("format", out _).Should().BeFalse();
        properties.GetProperty("card").TryGetProperty("format", out _).Should().BeFalse();
    }

    [Fact]
    public void Under_legacy_naming_a_body_property_keeps_the_format_of_a_data_type_attribute()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        var properties = Properties(document, "SubmitBodyFormat");

        properties.GetProperty("email").GetProperty("format").GetString().Should().Be("email");
        properties.GetProperty("phone").GetProperty("format").GetString().Should().Be("phone");
        properties.GetProperty("url").GetProperty("format").GetString().Should().Be("uri");
    }

    // The integration app serialises responses with CLR property names, so the response component's properties are
    // PascalCase where the body's are the generated binding's camelCase.
    [Fact]
    public void Under_legacy_naming_a_components_property_follows_the_same_rule()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        var properties = Properties(document, "BodyFormatResult");

        properties.GetProperty("BlueprintEmail").TryGetProperty("format", out _).Should().BeFalse();
        properties.GetProperty("Email").GetProperty("format").GetString().Should().Be("email");
    }

    [Fact]
    public void Under_default_naming_every_validation_attribute_keeps_its_format()
    {
        using var document = Render(OpenApiSchemaNaming.Default);

        var properties = Properties(document, "SubmitBodyFormat");

        properties.GetProperty("blueprintEmail").GetProperty("format").GetString().Should().Be("email");
        properties.GetProperty("blueprintUrl").GetProperty("format").GetString().Should().Be("uri");
        properties.GetProperty("card").GetProperty("format").GetString().Should().Be("credit-card");
        Properties(document, "BodyFormatResult").GetProperty("BlueprintEmail").GetProperty("format").GetString().Should().Be("email");
    }

    private static JsonDocument Render(OpenApiSchemaNaming naming)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(
            new IntegrationTestBlueprintApp(),
            new OpenApiInfoOptions { SchemaNaming = naming });

        return JsonDocument.Parse(rendered.Json);
    }

    private static JsonElement Properties(JsonDocument document, string schemaName)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(schemaName).GetProperty("properties");
    }
}

/// <summary>Submits strings carrying each format-implying validation attribute.</summary>
[AllowAnonymous]
[RootLink("/legacy-shape/body-format")]
[HttpPost]
public partial class SubmitBodyFormatCommand : ICommand<BodyFormatResult>
{
    [Blueprint.Validation.EmailAddress]
    public string? BlueprintEmail { get; set; }

    [Blueprint.Validation.Url]
    public string? BlueprintUrl { get; set; }

    [CreditCard]
    public string? Card { get; set; }

    [EmailAddress]
    public string? Email { get; set; }

    [Phone]
    public string? Phone { get; set; }

    [Url]
    public string? Url { get; set; }

    public static BodyFormatResult Handle(SubmitBodyFormatCommand command)
    {
        return new BodyFormatResult { BlueprintEmail = command.BlueprintEmail, Email = command.Email };
    }
}

/// <summary>The addresses a <see cref="SubmitBodyFormatCommand" /> submitted.</summary>
public sealed class BodyFormatResult
{
    [Blueprint.Validation.EmailAddress]
    public string? BlueprintEmail { get; init; }

    [EmailAddress]
    public string? Email { get; init; }
}
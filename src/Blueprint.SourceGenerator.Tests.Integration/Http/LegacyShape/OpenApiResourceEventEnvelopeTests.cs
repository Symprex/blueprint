using System.Text.Json;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves that an operation whose handler returns a <c>Blueprint.Http.ResourceEvent&lt;T&gt;</c> documents the event it
/// sends — the host writes the whole event, not its data — as a <c>ResourceEventOf{T}</c> component, as legacy Blueprint
/// did, rather than advertising the bare resource.
/// </summary>
/// <remarks>
/// The envelope's properties are legacy's <c>ResourceEventOfPartnerTenant</c>, from Signature365's committed Partners
/// document, and so is its status, 200, which the host sends for every legacy resource event.
/// </remarks>
public class OpenApiResourceEventEnvelopeTests
{
    private const string CreateTenantPath = "/legacy-shape/partners/{PartnerId}/tenants";

    [Fact]
    public void A_resource_event_response_references_its_envelope_component()
    {
        using var document = Render(static _ => { });

        SuccessSchemaRef(document).Should().Be("#/components/schemas/ResourceEventOfLegacyShapeTenantApiResource");
    }

    [Fact]
    public void The_envelope_carries_the_event_with_its_data_referencing_the_resource()
    {
        using var document = Render(static _ => { });
        var envelope = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("ResourceEventOfLegacyShapeTenantApiResource");
        var properties = envelope.GetProperty("properties");

        properties.EnumerateObject().Select(static p => p.Name).Should().BeEquivalentTo(
            ["$object", "eventId", "changeType", "created", "resourceObject", "href", "changedValues", "metadata", "secureData", "correlationId", "data"]);
        envelope.GetProperty("required").EnumerateArray().Select(static r => r.GetString()).Should().BeEquivalentTo(
            ["$object", "eventId", "changeType", "created", "resourceObject", "data"]);
        properties.GetProperty("data").GetProperty("$ref").GetString().Should().Be("#/components/schemas/LegacyShapeTenantApiResource");
        document.RootElement.GetProperty("components").GetProperty("schemas").TryGetProperty("ResourceEventChangeType", out _).Should().BeTrue();
    }

    [Fact]
    public void Under_legacy_naming_the_formatter_names_the_envelope_as_legacy_did()
    {
        using var document = Render(static options =>
        {
            options.SchemaNaming = OpenApiSchemaNaming.Legacy;
            options.SchemaNameFormatter = static name => name.Replace("ApiResource", string.Empty, StringComparison.Ordinal).Replace("PagedOf", "Paged", StringComparison.Ordinal);
        });

        SuccessSchemaRef(document).Should().Be("#/components/schemas/ResourceEventOfLegacyShapeTenant");
    }

    [Fact]
    public void Under_legacy_naming_changeType_and_href_carry_legacys_own_worded_descriptions()
    {
        using var document = Render(static options => options.SchemaNaming = OpenApiSchemaNaming.Legacy);
        var properties = EnvelopeProperties(document, "ResourceEventOfLegacyShapeTenantApiResource");

        properties.GetProperty("changeType").GetProperty("description").GetString().Should().Be(
            "Gets a the change type of this event, which is a higher-level version of the Type property,\n"
            + "indicating whether the resource was updated, created or deleted.");
        properties.GetProperty("href").GetProperty("description").GetString().Should().Be(
            "Gets or sets the href of the resource this event represents, to be populated by the ResourceEventMiddleware\n"
            + "component from the resource type and id definition object.");
    }

    [Fact]
    public void Under_default_naming_changeType_and_href_carry_the_frameworks_own_neutral_descriptions()
    {
        using var document = Render(static _ => { });
        var properties = EnvelopeProperties(document, "ResourceEventOfLegacyShapeTenantApiResource");

        properties.GetProperty("changeType").GetProperty("description").GetString().Should().Be(
            "Gets the change type of this event, indicating whether the resource was created, updated or deleted.");
        properties.GetProperty("href").GetProperty("description").GetString().Should().Be(
            "Gets or sets the href of the resource this event represents, populated by the framework from the resource type's\n"
            + "self link and the event's data or self query.");
    }

    private static JsonElement EnvelopeProperties(JsonDocument document, string componentName)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(componentName).GetProperty("properties");
    }

    private static string? SuccessSchemaRef(JsonDocument document)
    {
        return document.RootElement.GetProperty("paths").GetProperty(CreateTenantPath).GetProperty("post")
            .GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("$ref").GetString();
    }

    private static JsonDocument Render(Action<OpenApiInfoOptions> configure)
    {
        var options = new OpenApiInfoOptions();
        configure(options);

        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), options);

        return JsonDocument.Parse(rendered.Json);
    }
}

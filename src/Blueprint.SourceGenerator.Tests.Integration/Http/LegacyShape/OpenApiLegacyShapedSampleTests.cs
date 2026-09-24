using System.Text.Json;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Pins the legacy-shaped sample as Signature365 would configure it — legacy naming, request bodies keyed by type,
/// query parameters named by property, and its own <c>Replace("ApiResource", "").Replace("PagedOf", "Paged")</c> — against
/// the document legacy Blueprint produced for the same shapes.
/// </summary>
/// <remarks>
/// Every expected fact is taken from legacy: names from NJsonSchema 11.1 run on the same shapes of type, then that
/// rule; property shapes from Signature365's committed Partners document (<c>PartnerTenant</c>,
/// <c>PagedPartnerCompany</c>, <c>PartnerListOfCountry</c>, <c>ResourceEventOfPartnerTenant</c>,
/// <c>CreatePartnerTenantCommand</c>, <c>GET /partners</c>'s parameters). The one departure is a created event's status,
/// 201, which is what the host sends.
/// </remarks>
public class OpenApiLegacyShapedSampleTests
{
    private const string Schemas = "#/components/schemas/";

    [Fact]
    public void The_sample_has_the_components_legacy_gave_it()
    {
        using var document = Render();

        ComponentNames(document).Where(static name => name.Contains("LegacyShape", StringComparison.Ordinal)).Should().BeEquivalentTo(
        [
            "PagedLegacyShapeTenant",
            "LegacyShapeTenant",
            "LegacyShapeBillingAccount",
            "LegacyShapeAddress",
            "LegacyShapeClosedInfo",
            "LegacyShapeContact",
            "LegacyShapeStatus",
            "LegacyShapeTier",
            "LegacyShapeListOfLegacyShapeCountry",
            "LegacyShapeCountry",
            "LegacyShapeRun",
            "ResourceEventOfLegacyShapeTenant",
            "CreateLegacyShapeTenantCommand",
        ]);
    }

    [Fact]
    public void Its_operations_reference_those_components_and_name_their_parameters_as_legacy_did()
    {
        using var document = Render();
        var paths = document.RootElement.GetProperty("paths");

        var getTenants = paths.GetProperty("/legacy-shape/tenants").GetProperty("get");
        ResponseRef(getTenants, "200").Should().Be(Schemas + "PagedLegacyShapeTenant");
        getTenants.GetProperty("parameters").EnumerateArray().Select(static p => p.GetProperty("name").GetString()).Should().BeEquivalentTo(["Page", "PageSize"]);

        var createTenant = paths.GetProperty("/legacy-shape/partners/{PartnerId}/tenants").GetProperty("post");
        createTenant.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema")
            .GetProperty("$ref").GetString().Should().Be(Schemas + "CreateLegacyShapeTenantCommand");
        ResponseRef(createTenant, "201").Should().Be(Schemas + "ResourceEventOfLegacyShapeTenant");

        ResponseRef(paths.GetProperty("/legacy-shape/countries").GetProperty("get"), "200").Should().Be(Schemas + "LegacyShapeListOfLegacyShapeCountry");
    }

    [Fact]
    public void Its_nested_objects_are_referenced_as_legacy_referenced_them()
    {
        using var document = Render();
        var tenant = Component(document, "LegacyShapeTenant").GetProperty("properties");

        tenant.GetProperty("BillingAccount").GetProperty("$ref").GetString().Should().Be(Schemas + "LegacyShapeBillingAccount");

        var closedInfo = tenant.GetProperty("ClosedInfo");
        closedInfo.GetProperty("nullable").GetBoolean().Should().BeTrue();
        closedInfo.GetProperty("oneOf")[0].GetProperty("$ref").GetString().Should().Be(Schemas + "LegacyShapeClosedInfo");

        var ownerStatus = tenant.GetProperty("OwnerStatus");
        ownerStatus.GetProperty("nullable").GetBoolean().Should().BeTrue();
        ownerStatus.GetProperty("oneOf")[0].GetProperty("$ref").GetString().Should().Be(Schemas + "LegacyShapeStatus");

        Component(document, "PagedLegacyShapeTenant").GetProperty("properties").GetProperty("Values")
            .GetProperty("items").GetProperty("$ref").GetString().Should().Be(Schemas + "LegacyShapeTenant");
        Component(document, "LegacyShapeListOfLegacyShapeCountry").GetProperty("properties").GetProperty("Values")
            .GetProperty("items").GetProperty("$ref").GetString().Should().Be(Schemas + "LegacyShapeCountry");
        Component(document, "ResourceEventOfLegacyShapeTenant").GetProperty("properties").GetProperty("data")
            .GetProperty("$ref").GetString().Should().Be(Schemas + "LegacyShapeTenant");
        Component(document, "CreateLegacyShapeTenantCommand").GetProperty("properties").GetProperty("address")
            .GetProperty("oneOf")[0].GetProperty("$ref").GetString().Should().Be(Schemas + "LegacyShapeAddress");
    }

    private static string? ResponseRef(JsonElement operation, string status)
    {
        return operation.GetProperty("responses").GetProperty(status).GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("$ref").GetString();
    }

    private static JsonElement Component(JsonDocument document, string name)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(name);
    }

    private static IReadOnlyList<string> ComponentNames(JsonDocument document)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas").EnumerateObject().Select(static p => p.Name).ToList();
    }

    private static JsonDocument Render()
    {
        var options = new OpenApiInfoOptions
        {
            SchemaNaming = OpenApiSchemaNaming.Legacy,
            RequestBodyNaming = OpenApiRequestBodyNaming.OperationType,
            QueryParameterNaming = OpenApiParameterNaming.PropertyName,
            SchemaNameFormatter = static name => name.Replace("ApiResource", string.Empty, StringComparison.Ordinal).Replace("PagedOf", "Paged", StringComparison.Ordinal),
        };

        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), options);

        return JsonDocument.Parse(rendered.Json);
    }
}

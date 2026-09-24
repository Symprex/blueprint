using System.Text.Json;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves that an object nested in a body — directly, in a collection, or in another nested object — is a schema
/// component of its own that its property references, as legacy Blueprint made it, rather than a bare
/// <c>{"type": "object"}</c> a generated client can say nothing about.
/// </summary>
/// <remarks>
/// The integration app serialises with CLR property names, so its response schemas do; request bodies take their names from the
/// generated binding. The expected property shapes are legacy's own, from Signature365's committed Partners document: a required nested
/// object is a reference (<c>PartnerTenant.billingAccount</c>), a nullable one is <c>nullable</c> with the reference in
/// <c>oneOf</c> (<c>PartnerTenant.closedInfo</c>), and a collection's items are a reference
/// (<c>PartnerListOfCountry.values</c>).
/// </remarks>
public class OpenApiNestedComponentTests
{
    [Fact]
    public void A_nested_object_is_a_component_its_property_references()
    {
        using var document = Render();
        var tenant = Component(document, "LegacyShapeTenantApiResource").GetProperty("properties");

        RefOnly(tenant.GetProperty("BillingAccount")).Should().Be(Ref("LegacyShapeBillingAccount"));
        ComponentNames(document).Should().Contain(["LegacyShapeBillingAccount", "LegacyShapeClosedInfo", "LegacyShapeContact", "LegacyShapeAddress"]);
    }

    [Fact]
    public void A_nullable_nested_object_is_nullable_with_its_reference_in_one_of()
    {
        using var document = Render();
        var closedInfo = Component(document, "LegacyShapeTenantApiResource").GetProperty("properties").GetProperty("ClosedInfo");

        closedInfo.GetProperty("nullable").GetBoolean().Should().BeTrue();
        closedInfo.GetProperty("oneOf").EnumerateArray().Select(RefOnly).Should().Equal(Ref("LegacyShapeClosedInfo"));
        closedInfo.TryGetProperty("type", out _).Should().BeFalse("the reference, not an inline object, describes it");
    }

    [Fact]
    public void A_collection_of_nested_objects_has_items_that_reference_their_component()
    {
        using var document = Render();

        var contacts = Component(document, "LegacyShapeTenantApiResource").GetProperty("properties").GetProperty("Contacts");
        RefOnly(contacts.GetProperty("items")).Should().Be(Ref("LegacyShapeContact"));

        // An item type no response returns on its own, reachable only through a generic list's values.
        var values = Component(document, "LegacyShapeListApiResourceOfLegacyShapeCountry").GetProperty("properties").GetProperty("Values");
        RefOnly(values.GetProperty("items")).Should().Be(Ref("LegacyShapeCountryApiResource"));
    }

    [Fact]
    public void An_object_nested_in_a_nested_object_is_a_component_too()
    {
        using var document = Render();
        var address = Component(document, "LegacyShapeBillingAccount").GetProperty("properties").GetProperty("Address");

        address.GetProperty("nullable").GetBoolean().Should().BeTrue();
        address.GetProperty("oneOf").EnumerateArray().Select(RefOnly).Should().Equal(Ref("LegacyShapeAddress"));
    }

    [Fact]
    public void A_nested_object_in_a_request_body_references_its_component()
    {
        using var document = Render();
        var address = Component(document, "CreateLegacyShapeTenant").GetProperty("properties").GetProperty("address");

        address.GetProperty("nullable").GetBoolean().Should().BeTrue();
        address.GetProperty("oneOf").EnumerateArray().Select(RefOnly).Should().Equal(Ref("LegacyShapeAddress"));
    }

    private static string Ref(string component)
    {
        return $"#/components/schemas/{component}";
    }

    /// <summary>The target of a schema that is a reference and nothing else, or a description of what it is instead.</summary>
    private static string RefOnly(JsonElement schema)
    {
        var members = schema.EnumerateObject().Select(static p => p.Name).ToList();

        return members is ["$ref"] ? schema.GetProperty("$ref").GetString()! : "not a bare reference: " + schema.GetRawText();
    }

    private static JsonDocument Render()
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());

        return JsonDocument.Parse(rendered.Json);
    }

    private static JsonElement Component(JsonDocument document, string name)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(name);
    }

    private static IReadOnlyList<string> ComponentNames(JsonDocument document)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas").EnumerateObject().Select(static p => p.Name).ToList();
    }
}

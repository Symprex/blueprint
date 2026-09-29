using System.Text.Json;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves that under Legacy naming the properties of a schema component, of a request body and of a resource event
/// envelope are in the order legacy Blueprint's document had them, while Default naming keeps them in ordinal order.
/// </summary>
/// <remarks>
/// <para>
/// The order is what NJsonSchema took from Newtonsoft.Json's contract in legacy: the properties each base type declares,
/// from the nearest base to the root, and then the type's own, each in declaration order. A property a base type
/// declares virtual takes the position of its first declaration, so <c>$object</c> is among <c>ApiResource</c>'s. It was
/// established against the declaration order of the C# type behind each of the 518 components of Signature365's 18
/// legacy documents (<c>8b40ecc961</c>) that could be matched to one and had two or more properties in common: 486
/// have no base type with properties to tell the rules apart and 29 do and fit only this, which the root's properties
/// first misses three of and each type's own first misses altogether. Three are unmatched, two of them because the type
/// matched is not the one documented.
/// </para>
/// <para>
/// The generated client emits a model's fields and its validators in the document's order, so an order of another kind
/// reorders every generated interface. The expected orders below are legacy's own: <c>Paged*</c> and <c>ListOf*</c>
/// (<c>values,total,$object,$links,$partial,pageSize,currentPage</c>, and <c>$object,$links,$partial,values,total</c>),
/// <c>ResourceEventOf*</c>, <c>ProblemDetails</c> and <c>ValidationProblemDetails</c> from the documents themselves.
/// </para>
/// </remarks>
public sealed class OpenApiLegacyPropertyOrderTests
{
    [Fact]
    public void Under_legacy_naming_a_resources_own_properties_follow_its_base_types_in_declaration_order()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        PropertyNames(document, "LegacyShapeTenant").Should().Equal(
            "$object", "$links", "$partial", "TenantId", "BillingAccount", "ClosedInfo", "OwnerStatus", "Tier", "Contacts");
    }

    [Fact]
    public void Under_legacy_naming_a_paged_resource_lists_its_bases_from_the_nearest_and_then_its_own()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        PropertyNames(document, "PagedLegacyShapeTenant").Should().Equal(
            "Values", "Total", "$object", "$links", "$partial", "PageSize", "CurrentPage");
    }

    [Fact]
    public void Under_legacy_naming_a_list_resource_of_the_consumers_own_lists_its_list_base_before_the_resource_base()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        PropertyNames(document, "LegacyShapeListOfLegacyShapeCountry").Should().Equal("Values", "Total", "$object", "$links", "$partial");
    }

    [Theory]
    [InlineData("ProblemDetails", new[] { "type", "title", "status", "detail", "instance", "extensions" })]
    [InlineData("ValidationProblemDetails", new[] { "type", "title", "status", "detail", "instance", "extensions", "errors" })]
    public void Under_legacy_naming_a_problems_properties_are_in_declaration_order(string component, string[] expected)
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        PropertyNames(document, component).Should().Equal(expected);
    }

    [Fact]
    public void Under_legacy_naming_a_resource_event_envelope_ends_with_its_data()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        PropertyNames(document, "ResourceEventOfLegacyShapeTenant").Should().Equal(
            "$object", "eventId", "changeType", "created", "resourceObject", "href", "changedValues", "metadata", "secureData", "correlationId", "data");
    }

    [Theory]
    [InlineData(OpenApiSchemaNaming.Default)]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    public void A_request_body_lists_the_operations_own_properties_in_declaration_order(OpenApiSchemaNaming naming)
    {
        // PartnerId is a route property, so it is not in the body; alphabetical order would put address first.
        using var document = Render(naming);

        PropertyNames(document, "CreateLegacyShapeTenant").Should().Equal("companyName", "address", "tier");
    }

    [Fact]
    public void Under_legacy_naming_a_request_body_lists_what_it_inherits_before_its_own_properties()
    {
        // The route, header, cookie and query properties StopLegacyInheritedRouteCommand inherits stay in its body under
        // Legacy naming (R153), ahead of the Reason it declares, whichever order a walk of the operation's type gives.
        using var document = Render(OpenApiSchemaNaming.Legacy);

        PropertyNames(document, "StopLegacyInheritedRoute").Should().Equal("tenantId", "id", "headerValue", "cookieValue", "queryValue", "reason");
    }

    [Fact]
    public void Under_default_naming_the_properties_of_a_component_stay_in_ordinal_order()
    {
        using var document = Render(OpenApiSchemaNaming.Default);

        var names = PropertyNames(document, "LegacyShapeTenantApiResource");

        names.Should().Equal(names.OrderBy(static name => name, StringComparer.Ordinal));
        PropertyNames(document, "PagedApiResourceOfLegacyShapeTenant").Should().Equal(
            "$links", "$object", "$partial", "CurrentPage", "PageSize", "Total", "Values");
    }

    [Fact]
    public void Under_default_naming_a_request_body_lists_what_it_inherits_after_its_own_properties()
    {
        using var document = Render(OpenApiSchemaNaming.Default);

        PropertyNames(document, "StopLegacyInheritedRoute").Should().Equal("reason");
    }

    [Fact]
    public void The_order_lists_each_base_types_properties_from_the_nearest_base_and_then_the_types_own()
    {
        OpenApiPropertyOrder.GetLegacyOrder(typeof(OrderLeaf)).Should().Equal("MiddleA", "MiddleB", "RootA", "RootB", "LeafA", "LeafB");
    }

    [Fact]
    public void The_order_lists_an_override_where_its_property_was_first_declared()
    {
        // RootB is overridden in the leaf, and keeps its place among the root's.
        OpenApiPropertyOrder.GetLegacyOrder(typeof(OrderOverridingLeaf)).Should().Equal("MiddleA", "MiddleB", "RootA", "RootB", "LeafA");
    }

    [Fact]
    public void The_order_lists_a_property_that_hides_another_where_the_hiding_type_declares_it()
    {
        // MiddleA is hidden by the leaf's own, which so comes last.
        OpenApiPropertyOrder.GetLegacyOrder(typeof(OrderHidingLeaf)).Should().Equal("MiddleB", "RootA", "RootB", "LeafA", "MiddleA");
    }

    [Fact]
    public void The_order_leaves_out_an_indexer_and_a_private_property()
    {
        OpenApiPropertyOrder.GetLegacyOrder(typeof(OrderWithIndexer)).Should().Equal("Visible");
    }

    private static string[] PropertyNames(JsonDocument document, string component)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(component)
            .GetProperty("properties").EnumerateObject().Select(static property => property.Name).ToArray();
    }

    private static JsonDocument Render(OpenApiSchemaNaming naming)
    {
        // Legacy's own schema names, as legacy's builder shortened them, so that a component is found by the name legacy
        // gave it.
        var options = new OpenApiInfoOptions
        {
            SchemaNaming = naming,
            SchemaNameFormatter = naming == OpenApiSchemaNaming.Legacy
                ? static name => name.Replace("ApiResource", string.Empty, StringComparison.Ordinal).Replace("PagedOf", "Paged", StringComparison.Ordinal)
                : null,
        };

        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), options);

        return JsonDocument.Parse(rendered.Json);
    }
}

public class OrderRoot
{
    public int RootA { get; set; }

    public virtual int RootB { get; set; }
}

public class OrderMiddle : OrderRoot
{
    public int MiddleA { get; set; }

    public int MiddleB { get; set; }
}

public class OrderLeaf : OrderMiddle
{
    public int LeafA { get; set; }

    public int LeafB { get; set; }
}

public class OrderOverridingLeaf : OrderMiddle
{
    public override int RootB { get; set; }

    public int LeafA { get; set; }
}

public class OrderHidingLeaf : OrderMiddle
{
    public int LeafA { get; set; }

    public new int MiddleA { get; set; }
}

public class OrderWithIndexer
{
    public int Visible { get; set; }

    public int this[int index] => index;

    private int Hidden { get; set; }
}
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that the document describes what legacy Blueprint described, from the XML documentation file beside the
/// declaring assembly: a response component and its properties, a request body and its properties, an enum and a
/// parameter, each formatted as legacy formatted it, and the framework's own resources and envelope in legacy's words.
/// </summary>
/// <remarks>
/// <para>
/// Every expected description of the documented fixtures (<c>OpenApiXmlDocTests.Fixtures.cs</c>) is legacy's: NJsonSchema
/// 11.1 run with legacy's settings on the same types, for a schema and a property, and legacy's
/// <c>Blueprint.Utilities.XmlDocsExtensions</c> run on the same properties, for a parameter. Every expected description of
/// the framework's resources and envelope is Signature365's committed Partners document's.
/// </para>
/// <para>
/// Legacy read only XML documentation, and so a response component had none of its own before: only operations and their
/// properties were described, from documentation the generator read at compile time.
/// </para>
/// </remarks>
public sealed class OpenApiXmlDocTests
{
    [Theory]
    [InlineData("XmlDocShape", null, "A type whose summary\nspans two lines.")]
    [InlineData("XmlDocShape", "Single", "A single line.")]
    [InlineData("XmlDocShape", "MultiLine", "First line.\nSecond line.\n            \n    Indented line.")]
    [InlineData("XmlDocShape", "Crefs", "Refers to XmlDocChild, List`1, Single, null, https://example.test and a child.")]
    [InlineData("XmlDocShape", "Elements", "Uses code and a paragraph.")]
    [InlineData("XmlDocShape", "Escapes", "Tom & Jerry <3 \"quoted\" 'single'.")]
    [InlineData("XmlDocShape", "FromBase", "Base summary.")]
    [InlineData("XmlDocShape", "BaseOnly", "Declared on the base only.")]
    [InlineData("XmlDocShape", "FromInterface", "Interface summary.")]
    [InlineData("XmlDocShape", "BothDescribed", "The attribute.")]
    [InlineData("XmlDocShape", "WithRemarks", "The summary.")]
    [InlineData("XmlDocShape", "Undocumented", null)]
    [InlineData("XmlDocShape", "Colour", null)]
    [InlineData("XmlDocShape", "DocumentedColour", "A documented colour.")]
    [InlineData("XmlDocShape", "DocumentedChild", "A documented child reference.")]
    [InlineData("XmlDocShape", "Generic", "A generic.")]
    [InlineData("XmlDocShape", "Described", null)]
    [InlineData("XmlDocShape", "Derived", null)]
    [InlineData("XmlDocColour", null, "A colour.")]
    [InlineData("XmlDocChild", null, "A child.")]
    [InlineData("XmlDocChild", "Name", "The name.")]
    [InlineData("XmlDocGenericOfXmlDocChild", null, "A generic of , see XmlDocGeneric`1.")]
    [InlineData("XmlDocGenericOfXmlDocChild", "Value", "The value.")]
    [InlineData("XmlDocDescribedType", null, "Type attribute.")]
    [InlineData("XmlDocDescribedType", "X", null)]
    [InlineData("XmlDocDerived", null, null)]
    [InlineData("XmlDocDerived", "FromBase", "Base summary.")]
    [InlineData("XmlDocDerived", "Own", "Own property.")]
    [InlineData("DescribeXmlDocShape", null, "Describes a shape,\nover two lines.")]
    [InlineData("DescribeXmlDocShape", "note", "A note over\ntwo lines, Tom & Jerry.")]
    [InlineData("DescribeXmlDocShape", "noteColour", "The colour of the note.")]
    [InlineData("XmlDocNoteColour", null, "A note colour, which only a request body refers to.")]
    public void A_schema_and_its_properties_are_described_as_legacy_described_them(string component, string? property, string? expected)
    {
        using var document = Render();

        Description(Schema(document, component, property)).Should().Be(expected);
    }

    [Theory]
    [InlineData("/xml-docs", "get", "Filter", "Filters by\ntwo lines, Tom &amp; Jerry.")]
    [InlineData("/xml-docs", "get", "Page", "The page, declared on the base.")]
    [InlineData("/xml-docs/{Id}", "post", "Id", "The route id.")]
    public void A_parameter_is_described_as_legacy_described_it(string path, string method, string parameter, string expected)
    {
        using var document = Render();
        var parameters = document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method).GetProperty("parameters");

        parameters.EnumerateArray()
            .Single(p => string.Equals(p.GetProperty("name").GetString(), parameter, StringComparison.OrdinalIgnoreCase))
            .GetProperty("description").GetString()
            .Should().Be(expected);
    }

    // changeType/href: R26 gates legacy's own worded descriptions (typo included) to Legacy naming only (proven in
    // OpenApiResourceEventEnvelopeTests); Render() below uses Default options, so these two carry the framework's own
    // neutral summaries instead.
    [Theory]
    [InlineData(null, "A typed subclass of ResourceEvent that redefines the Data type to\nbe of a specific type, useful for better type safety when dealing with events throughout the code\nbase.")]
    [InlineData("$object", "Gets the object of this 'resource', which is \"event\".")]
    [InlineData("eventId", "Gets the id of this event, for example 'timeEntry.updated' or 'account.approvals.enabled'.")]
    [InlineData("changeType", "Gets the change type of this event, indicating whether the resource was created, updated or deleted.")]
    [InlineData("created", "Gets the created date of this event.")]
    [InlineData("resourceObject", "Gets the type of resource represented.")]
    [InlineData("href", "Gets or sets the href of the resource this event represents, populated by the framework from the resource type's\nself link and the event's data or self query.")]
    [InlineData("changedValues", "Gets or sets a dictionary of previous values, used to identify what has actually changed\nbetween two resources, keyed on the property name with the previous value of the resource\nbeing the value.")]
    [InlineData("metadata", "Gets the metadata dictionary of this resource event, a simple bag of key value pairs\nof useful information that can be stored free-form against an event.")]
    [InlineData("secureData", "Gets a dictionary of security-related data that will NOT be persisted anywhere.")]
    [InlineData("correlationId", "Gets or sets the correlation id for this event, which can be used to tie it back to\nthe initial request that resulted in an event.")]
    [InlineData("data", null)]
    public void A_resource_event_envelope_is_described_in_legacy_words(string? property, string? expected)
    {
        using var document = Render();

        Description(Schema(document, "ResourceEventOfLegacyEventWidgetApiResource", property)).Should().Be(expected);
    }

    [Theory]
    [InlineData("PagedApiResourceOfLegacyShapeTenant", null, "A specialisation of ListApiResource`1 that represents a list of resources\nthat can be paged on the server.")]
    [InlineData("PagedApiResourceOfLegacyShapeTenant", "Values", "The values of this list.")]
    [InlineData("PagedApiResourceOfLegacyShapeTenant", "Total", "The total count of values in this list.")]
    [InlineData("PagedApiResourceOfLegacyShapeTenant", "PageSize", "The size of the page requested.")]
    [InlineData("PagedApiResourceOfLegacyShapeTenant", "CurrentPage", "The 1-based index of the page requested.")]
    [InlineData("PagedApiResourceOfLegacyShapeTenant", "$object", "The object type of this resource, used to indicate to clients what they\nare dealing with (i.e. 'user', 'account', 'group').")]
    [InlineData("PagedApiResourceOfLegacyShapeTenant", "$links", "The links for this resource, other endpoints that apply to this resource\nin it's current state.")]
    [InlineData("PagedApiResourceOfLegacyShapeTenant", "$partial", "Gets or sets a value indicating whether or not this resource is 'partial', an indication that not all properties have been\npopulated for performance reasons and a client should reload from the 'self' link to retrieve all values.")]
    [InlineData("Link", null, "A link from one resource to another, that indicates what operations may be available\nfor a given ILinkableResource being returned from the API.")]
    [InlineData("Link", "href", "The URL of this link.")]
    [InlineData("Link", "type", "The type of resource this link will return.")]
    public void A_framework_resource_is_described_in_legacy_words(string component, string? property, string expected)
    {
        using var document = Render();

        Description(Schema(document, component, property)).Should().Be(expected);
    }

    private static JsonDocument Render()
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());

        return JsonDocument.Parse(rendered.Json);
    }

    /// <summary>The named component, or the named property of it.</summary>
    private static JsonElement Schema(JsonDocument document, string component, string? property)
    {
        var schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(component);

        return property is null ? schema : schema.GetProperty("properties").GetProperty(property);
    }

    private static string? Description(JsonElement schema)
    {
        return schema.TryGetProperty("description", out var description) ? description.GetString() : null;
    }
}

/// <summary>Gets the documented shape.</summary>
[AllowAnonymous]
[RootLink("/xml-docs")]
public partial class GetXmlDocShapeQuery : XmlDocQueryBase, IQuery<XmlDocShape>
{
    /// <summary>
    /// Filters by
    /// two lines, Tom &amp; Jerry.
    /// </summary>
    public string? Filter { get; set; }

    public static XmlDocShape Handle(GetXmlDocShapeQuery query)
    {
        return new XmlDocShape();
    }
}

/// <summary>
/// Describes a shape,
/// over two lines.
/// </summary>
[AllowAnonymous]
[RootLink("/xml-docs/{Id}")]
[HttpPost]
public partial class DescribeXmlDocShapeCommand : ICommand<XmlDocShape>
{
    /// <summary>The route id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// A note over
    /// two lines, Tom &amp; Jerry.
    /// </summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>The colour of the note.</summary>
    public XmlDocNoteColour NoteColour { get; set; }

    public static XmlDocShape Handle(DescribeXmlDocShapeCommand command)
    {
        return new XmlDocShape();
    }
}

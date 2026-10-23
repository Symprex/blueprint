using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves that under Legacy naming a property that is nothing but a reference to an enum is a bare <c>$ref</c>, and the
/// items of a collection whose elements may be null wrap their reference beside <c>nullable</c>, as legacy Blueprint's
/// document had them, while Default naming keeps wrapping the enum reference and leaves the items bare.
/// </summary>
/// <remarks>
/// <para>
/// In Signature365's 18 legacy documents (<c>8b40ecc961</c>) 81 properties are a bare <c>$ref</c> and every other
/// reference is a <c>oneOf</c> of one <c>$ref</c> beside what it needs: a description (384), a description and
/// <c>nullable</c> (247), <c>nullable</c> alone (2) or a deprecation (13). The framework wrote 79 bare and two wrapped
/// with nothing beside them: <c>InjectionBlockedDetails.reasonCode</c> and <c>SendingBlockedDetails.reasonCode</c>, whose
/// generated client fields' metadata differs for it. One collection has nullable items, Portal's
/// <c>UserSignaturePreview.signatures</c> (<c>IEnumerable&lt;UserSignaturePreviewBlock?&gt;</c>), whose items legacy wrote
/// as <c>{nullable: true, oneOf: [{$ref}]}</c>.
/// </para>
/// </remarks>
public sealed class OpenApiLegacyRefWrapperTests
{
    private const string StatusRef = "#/components/schemas/LegacyShapeStatus";

    [Fact]
    public void Under_legacy_naming_an_enum_property_with_nothing_beside_its_reference_is_a_bare_ref()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        JsonNode.Parse(Property(document, "Plain").GetRawText())!.ToJsonString().Should().Be("""{"$ref":"#/components/schemas/LegacyShapeStatus"}""");
    }

    [Fact]
    public void Under_legacy_naming_an_enum_property_that_is_nullable_or_described_still_wraps_its_reference()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        var optional = Property(document, "Optional");
        optional.GetProperty("nullable").GetBoolean().Should().BeTrue();
        optional.GetProperty("oneOf").EnumerateArray().Single().GetProperty("$ref").GetString().Should().Be(StatusRef);

        var described = Property(document, "Described");
        described.GetProperty("description").GetString().Should().Be("A described status.");
        described.GetProperty("oneOf").EnumerateArray().Single().GetProperty("$ref").GetString().Should().Be(StatusRef);
    }

    [Fact]
    public void Under_default_naming_an_enum_property_with_nothing_beside_its_reference_is_still_wrapped()
    {
        using var document = Render(OpenApiSchemaNaming.Default);

        Property(document, "Plain").GetProperty("oneOf").EnumerateArray().Single().GetProperty("$ref").GetString().Should().Be(StatusRef);
    }

    [Fact]
    public void Under_legacy_naming_the_items_of_a_collection_of_nullable_elements_wrap_their_reference_beside_nullable()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        JsonNode.Parse(Property(document, "Contacts").GetProperty("items").GetRawText())!.ToJsonString()
            .Should().Be("""{"nullable":true,"oneOf":[{"$ref":"#/components/schemas/LegacyShapeContact"}]}""");
    }

    [Fact]
    public void Under_legacy_naming_the_items_of_a_collection_of_elements_that_are_not_null_stay_a_bare_ref()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        JsonNode.Parse(Property(document, "RequiredContacts").GetProperty("items").GetRawText())!.ToJsonString()
            .Should().Be("""{"$ref":"#/components/schemas/LegacyShapeContact"}""");
    }

    [Fact]
    public void Under_default_naming_the_items_of_a_collection_of_nullable_elements_stay_a_bare_ref()
    {
        using var document = Render(OpenApiSchemaNaming.Default);

        JsonNode.Parse(Property(document, "Contacts").GetProperty("items").GetRawText())!.ToJsonString()
            .Should().Be("""{"$ref":"#/components/schemas/LegacyShapeContact"}""");
    }

    [Fact]
    public void Under_legacy_naming_a_request_body_has_the_same_bare_ref_and_nullable_items()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);
        var body = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("CreateRefWrapper").GetProperty("properties");

        JsonNode.Parse(body.GetProperty("plain").GetRawText())!.ToJsonString().Should().Be("""{"$ref":"#/components/schemas/LegacyShapeTier"}""");
        JsonNode.Parse(body.GetProperty("addresses").GetProperty("items").GetRawText())!.ToJsonString()
            .Should().Be("""{"nullable":true,"oneOf":[{"$ref":"#/components/schemas/LegacyShapeAddress"}]}""");
        JsonNode.Parse(body.GetProperty("requiredAddresses").GetProperty("items").GetRawText())!.ToJsonString()
            .Should().Be("""{"$ref":"#/components/schemas/LegacyShapeAddress"}""");
    }

    [Fact]
    public void Under_default_naming_a_request_body_keeps_the_wrapped_enum_and_bare_items()
    {
        using var document = Render(OpenApiSchemaNaming.Default);
        var body = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("CreateRefWrapper").GetProperty("properties");

        body.GetProperty("plain").GetProperty("oneOf").EnumerateArray().Single().GetProperty("$ref").GetString()
            .Should().Be("#/components/schemas/LegacyShapeTier");
        JsonNode.Parse(body.GetProperty("addresses").GetProperty("items").GetRawText())!.ToJsonString()
            .Should().Be("""{"$ref":"#/components/schemas/LegacyShapeAddress"}""");
    }

    private static JsonElement Property(JsonDocument document, string property)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("RefWrapperApiResource")
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

/// <summary>A resource with an enum property of each form and a collection of nullable and of non-null elements.</summary>
public partial class RefWrapperApiResource : ApiResource
{
    public LegacyShapeStatus Plain { get; init; }

    public LegacyShapeStatus? Optional { get; init; }

    [Description("A described status.")]
    public LegacyShapeStatus Described { get; init; }

    public List<LegacyShapeContact?> Contacts { get; init; } = [];

    public List<LegacyShapeContact> RequiredContacts { get; init; } = [];

    public double?[]? ScoreTrend { get; init; }

    public List<int?> Counts { get; init; } = [];

    public int[] Totals { get; init; } = [];

    public LegacyShapeScores Scores { get; init; } = [];

    public LegacyShapeIntValues<double?> IntValues { get; init; } = [];

    public List<LegacyShapeCoordinate?> Coordinates { get; init; } = [];

    public Dictionary<string, double?[]> ScoresByName { get; init; } = [];

    public Dictionary<string, int[]> TotalsByName { get; init; } = [];

    public Dictionary<string, LegacyShapeCoordinate?[]> CoordinatesByName { get; init; } = [];

    // A serialised field has no PropertyInfo.
#pragma warning disable SA1401 // The test needs a public field.
    [System.Text.Json.Serialization.JsonInclude]
    public double?[] ScoreField = [];
#pragma warning restore SA1401
}

/// <summary>A non-generic collection that inherits its nullable value type elements.</summary>
public sealed class LegacyShapeScores : List<double?>;

/// <summary>A generic collection whose own type argument is not its element type, which is always <see cref="int"/>.</summary>
/// <typeparam name="T">A type argument that is not the element type.</typeparam>
public sealed class LegacyShapeIntValues<T> : List<int>;

/// <summary>A struct that has a schema component of its own.</summary>
public readonly struct LegacyShapeCoordinate
{
    public double X { get; init; }

    public double Y { get; init; }
}

/// <summary>Returns the resource.</summary>
[AllowAnonymous]
[RootLink("/ref-wrappers")]
public partial class GetRefWrapperQuery : IQuery<RefWrapperApiResource>
{
    public static RefWrapperApiResource Handle(GetRefWrapperQuery query)
    {
        return new RefWrapperApiResource();
    }
}

/// <summary>A command whose body has the same forms.</summary>
[AllowAnonymous]
[RootLink("/ref-wrappers")]
[HttpPost]
public partial class CreateRefWrapperCommand : ICommand<string>
{
    public LegacyShapeTierKind Plain { get; set; }

    public List<LegacyShapeAddress?>? Addresses { get; set; }

    public List<LegacyShapeAddress> RequiredAddresses { get; set; } = [];

    public double?[]? ScoreTrend { get; set; }

    public List<int?> Counts { get; set; } = [];

    public int[] Totals { get; set; } = [];

    public LegacyShapeScores Scores { get; set; } = [];

    public LegacyShapeIntValues<double?> IntValues { get; set; } = [];

    public List<LegacyShapeCoordinate?> Coordinates { get; set; } = [];

    public static string Handle(CreateRefWrapperCommand command)
    {
        return "created";
    }
}

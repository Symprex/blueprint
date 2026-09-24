using System.Text.Json;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Sample;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a resource declared in another assembly than the app's serializer context is written, and documented,
/// with <c>$object</c> alone beside its own properties, as legacy Blueprint wrote it: not also as <c>Object</c>, and not
/// with the ignored <c>ResourceKey</c>.
/// </summary>
/// <remarks>
/// <para>
/// The generator gives a partial resource an <c>Object</c> override. The System.Text.Json generator of the assembly that
/// declares the resource cannot see it, but that of an app assembly referencing it can, and without the base property's
/// <c>[JsonPropertyName("$object")]</c> it took the override for a property of its own. Signature365's resources are
/// declared in contract assemblies: Partners' <c>PartnerCompanyApiResource</c>, serialised through the Partners app's
/// own context, used to write <c>"Object":"partnerCompany"</c> beside <c>"$object":"partnerCompany"</c>. That context also
/// described <c>ApiResource.ResourceKey</c>, whose <c>[JsonIgnore]</c> leaves it without a getter or setter; it was never
/// written, but the document listed it as a property.
/// </para>
/// <para>
/// Legacy wrote a resource through Newtonsoft.Json, which kept the base property's name for an override. Signature365's
/// committed documents list <c>$object</c>, <c>$links</c> and <c>$partial</c> and no <c>object</c> or
/// <c>resourceKey</c>. The sample app's resources are declared in its contract assembly, as Signature365's are.
/// </para>
/// </remarks>
public sealed class ApiResourceObjectPropertyTests
{
    private static readonly string[] _templateProperties =
    [
        "Id", "Name", "HtmlSource", "PlainTextSource", "IsArchived", "CreatedAt", "ModifiedAt", "DefaultSortOrder",
        "OverrideSortOrder", "Owner", "$object", "$links", "$partial",
    ];

    [Fact]
    public void A_resource_from_another_assembly_is_written_with_object_once()
    {
        var template = new TemplateApiResource("tmpl-1", "Welcome", "<p/>", string.Empty);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(template, SampleBlueprintAppJsonContext.Default.TemplateApiResource));

        json.RootElement.EnumerateObject().Select(static p => p.Name).Should().BeEquivalentTo(_templateProperties);
        json.RootElement.GetProperty("$object").GetString().Should().Be("template");
    }

    [Fact]
    public void A_resource_from_another_assembly_is_documented_with_object_once_and_without_its_ignored_key()
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new SampleBlueprintApp(), new OpenApiInfoOptions());
        using var document = JsonDocument.Parse(rendered.Json);
        var component = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("TemplateApiResource");

        component.GetProperty("properties").EnumerateObject().Select(static p => p.Name).Should().BeEquivalentTo(_templateProperties);
    }

    [Fact]
    public void Under_the_legacy_rule_a_resource_from_another_assembly_requires_its_object()
    {
        // Signature365's committed documents require $object of every resource, as legacy's contract resolver required
        // a property whose type is not nullable.
        var (_, rendered) = OpenApiDocumentEndpoint.Create(
            new SampleBlueprintApp(),
            new OpenApiInfoOptions { RequiredProperties = OpenApiRequiredProperties.NonNullable });
        using var document = JsonDocument.Parse(rendered.Json);
        var component = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("TemplateApiResource");

        component.GetProperty("required").EnumerateArray().Select(static name => name.GetString()).Should().Contain("$object");
    }
}

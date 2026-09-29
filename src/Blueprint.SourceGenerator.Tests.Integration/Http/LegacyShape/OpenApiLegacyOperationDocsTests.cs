using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves that under Legacy naming an operation's <c>summary</c> and <c>description</c> are its type's <c>summary</c> and
/// <c>remarks</c> read from the XML documentation file beside its assembly as legacy read them, and both are written,
/// empty where the operation has none, while Default naming writes only what the operation documents.
/// </summary>
/// <remarks>
/// <para>
/// Legacy set <c>Summary = XmlDocsExtensions.GetXmlDocsSummary(operation.OperationType)</c> and <c>Description =
/// GetXmlDocsRemarks(...)</c> (Signature365 <c>8b40ecc961</c>, <c>OpenApiDocumentBuilder.cs:103-104</c>), which read at run
/// time, kept line breaks, removed the first line's indentation, wrote a <c>&lt;see cref&gt;</c> as the last segment of its
/// target and escaped text as XML (<c>Blueprint/Utilities/XmlDocsExtensions.cs</c>). NSwag wrote the empty string legacy
/// gave an undocumented operation: Signature365's 18 legacy documents carry 590 empty descriptions.
/// </para>
/// <para>
/// The expected texts are legacy's algorithm applied by hand to the fixtures' comments below: the same reader as the
/// framework's parameter descriptions, whose expectations (<c>OpenApiXmlDocTests</c>) were taken from legacy's own code.
/// </para>
/// </remarks>
public sealed class OpenApiLegacyOperationDocsTests
{
    [Fact]
    public void Under_legacy_naming_the_summary_and_description_are_the_types_summary_and_remarks_as_legacy_read_them()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);
        var operation = Operation(document, "/legacy-operation-docs/documented", "get");

        operation.GetProperty("summary").GetString().Should().Be("Lists the XmlDocChild widgets,\nfor Tom &amp; Jerry &lt;3.");
        operation.GetProperty("description").GetString().Should().Be("Remarks over\ntwo lines, with code.");
    }

    [Fact]
    public void Under_legacy_naming_an_operation_without_remarks_has_an_empty_description()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);
        var operation = Operation(document, "/xml-docs/{Id}", "post");

        operation.GetProperty("summary").GetString().Should().Be("Describes a shape,\nover two lines.");
        operation.GetProperty("description").GetString().Should().BeEmpty();
    }

    [Fact]
    public void Under_legacy_naming_an_undocumented_operation_has_an_empty_summary_and_description()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);
        var operation = Operation(document, "/legacy-operation-docs/undocumented", "get");

        operation.GetProperty("summary").GetString().Should().BeEmpty();
        operation.GetProperty("description").GetString().Should().BeEmpty();
    }

    [Fact]
    public void Under_default_naming_an_undocumented_operation_has_neither()
    {
        using var document = Render(OpenApiSchemaNaming.Default);
        var operation = Operation(document, "/legacy-operation-docs/undocumented", "get");

        operation.TryGetProperty("summary", out _).Should().BeFalse();
        operation.TryGetProperty("description", out _).Should().BeFalse();
    }

    private static JsonElement Operation(JsonDocument document, string path, string method)
    {
        return document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method);
    }

    private static JsonDocument Render(OpenApiSchemaNaming naming)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(
            new IntegrationTestBlueprintApp(),
            new OpenApiInfoOptions { SchemaNaming = naming });

        return JsonDocument.Parse(rendered.Json);
    }
}

/// <summary>
/// Lists the <see cref="XmlDocChild"/> widgets,
/// for Tom &amp; Jerry &lt;3.
/// </summary>
/// <remarks>
/// Remarks over
/// two lines, with <c>code</c>.
/// </remarks>
[AllowAnonymous]
[RootLink("/legacy-operation-docs/documented")]
public partial class GetLegacyOperationDocsQuery : IQuery<string>
{
    public static string Handle(GetLegacyOperationDocsQuery query)
    {
        return "documented";
    }
}

[AllowAnonymous]
[RootLink("/legacy-operation-docs/undocumented")]
public partial class GetLegacyOperationUndocumentedQuery : IQuery<string>
{
    public static string Handle(GetLegacyOperationUndocumentedQuery query)
    {
        return "undocumented";
    }
}
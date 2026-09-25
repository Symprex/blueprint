using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that an operation published at more than one URL is given a distinct <c>operationId</c> at each, as legacy
/// Blueprint's document gave it.
/// </summary>
/// <remarks>
/// <para>
/// Legacy's builder set every route's <c>operationId</c> to the operation's name (Signature365 <c>8b40ecc961</c>,
/// <c>Blueprint.OpenApi/OpenApiDocumentBuilder.cs:94-109</c>), walking its <c>Links</c> in declaration order
/// (<c>Blueprint/Configuration/OperationScanner.cs:294-298</c>, every <c>LinkAttribute</c> kind). NSwag 14.2.0's
/// <c>OpenApiDocument.ToJson</c> then made the ids unique (<c>GenerateOperationIds</c>, called at <c>ToJson</c>), in the
/// document's path order:
/// </para>
/// <list type="number">
/// <item><description>in a group sharing an id, <c>All</c> is appended to each operation with a collection success
/// response, unless all of them have one;</description></item>
/// <item><description>then, in a group whose operations do not all share a method, the upper-cased method is appended to
/// each;</description></item>
/// <item><description>then a group is numbered, the first keeping its id and the next <c>2</c>, <c>3</c>, ..., and the
/// whole rule runs again.</description></item>
/// </list>
/// <para>
/// So the Portal's <c>CheckTenantDomainsCommand</c>, <c>[Link]</c>ed at <c>/domains/check</c> and then
/// <c>/add-ons/microsoft365/check-domains</c>, was <c>CheckTenantDomains</c> and <c>CheckTenantDomains2</c>, and the
/// Portal's client calls <c>checkTenantDomains2</c>. The Admin Portal's <c>AdminPortalGetAccountInjectionStatusQuery</c>
/// had <c>GetAccountInjectionStatus</c> and <c>GetAccountInjectionStatus2</c> in the same way (both in their committed
/// <c>Spec/openapi.json</c>).
/// </para>
/// </remarks>
public sealed class OperationIdDeduplicationTests
{
    [Fact]
    public void An_operation_at_two_urls_keeps_its_name_at_the_first_declared_and_is_numbered_at_the_second()
    {
        // Declared "z-first" before "a-second", so the ordinal order of the paths is the reverse of legacy's.
        using var document = Render();

        OperationId(document, "/operation-ids/z-first", "post").Should().Be("CheckOperationIdDomains");
        OperationId(document, "/operation-ids/a-second", "post").Should().Be("CheckOperationIdDomains2");
    }

    [Fact]
    public void An_operation_at_three_urls_is_numbered_in_declaration_order()
    {
        using var document = Render();

        OperationId(document, "/operation-ids/three/c", "get").Should().Be("GetOperationIdTriple");
        OperationId(document, "/operation-ids/three/a", "get").Should().Be("GetOperationIdTriple2");
        OperationId(document, "/operation-ids/three/b", "get").Should().Be("GetOperationIdTriple3");
    }

    [Fact]
    public void Every_operationId_in_the_document_is_unique()
    {
        using var document = Render();

        var operationIds = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(static path => path.Value.EnumerateObject().Select(static method => method.Value.GetProperty("operationId").GetString()))
            .ToList();

        operationIds.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void The_request_body_of_every_url_is_still_the_operations_one_schema()
    {
        // Legacy's body schema was the operation type's (OpenApiDocumentBuilder.cs:160-178), whatever the route's
        // operationId became.
        using var document = Render();

        var first = RequestBodyReference(document, "/operation-ids/z-first");
        var second = RequestBodyReference(document, "/operation-ids/a-second");

        first.Should().Be(second);
        first.Should().EndWith("/CheckOperationIdDomains");
    }

    /// <summary>
    /// Each case's expected ids are NSwag 14.2.0's own: an <c>OpenApiDocument</c> built with those operations, in that
    /// path order, and written with <c>ToJson()</c>.
    /// </summary>
    /// <param name="operations">Each operation as <c>id,METHOD,returns-a-collection</c>, in path order.</param>
    /// <param name="expected">The ids NSwag wrote, in the same order.</param>
    [Theory]
    [InlineData("X,POST,false|X,POST,false|X,POST,false", "X|X2|X3")]
    [InlineData("X,GET,false|X,GET,true", "X|XAll")]
    [InlineData("X,GET,true|X,GET,false", "XAll|X")]
    [InlineData("X,GET,true|X,GET,true", "X|X2")]
    [InlineData("X,GET,true|X,GET,false|XAll,GET,false", "XAll|X|XAll2")]
    [InlineData("X,POST,false|X,POST,false|X2,POST,false", "X|X2|X22")]
    [InlineData("X,PUT,false|X,POST,false", "XPUT|XPOST")]
    public void Ids_are_made_unique_as_NSwag_made_them(string operations, string expected)
    {
        var input = operations.Split('|')
            .Select(static o => o.Split(','))
            .Select(static parts => (parts[0], parts[1], bool.Parse(parts[2])))
            .ToList();

        LegacyOperationIds.GenerateOperationIds(input).Should().Equal(expected.Split('|'));
    }

    private static JsonDocument Render()
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());

        return JsonDocument.Parse(rendered.Json);
    }

    private static string? OperationId(JsonDocument document, string path, string method)
    {
        return document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method).GetProperty("operationId").GetString();
    }

    private static string? RequestBodyReference(JsonDocument document, string path)
    {
        return document.RootElement.GetProperty("paths").GetProperty(path).GetProperty("post").GetProperty("requestBody")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString();
    }
}

/// <summary>A command at two URLs, shaped as the Portal's <c>CheckTenantDomainsCommand</c>.</summary>
[RootLink("/operation-ids/z-first", Rel = "z-first")]
[RootLink("/operation-ids/a-second", Rel = "a-second")]
[HttpPost]
public partial class CheckOperationIdDomainsCommand : ICommand<string>
{
    public string? Domain { get; set; }

    public static string Handle(CheckOperationIdDomainsCommand command)
    {
        return "checked";
    }
}

/// <summary>A query at three URLs.</summary>
[RootLink("/operation-ids/three/c", Rel = "c")]
[RootLink("/operation-ids/three/a", Rel = "a")]
[RootLink("/operation-ids/three/b", Rel = "b")]
public partial class GetOperationIdTripleQuery : IQuery<string>
{
    public static string Handle(GetOperationIdTripleQuery query)
    {
        return "triple";
    }
}

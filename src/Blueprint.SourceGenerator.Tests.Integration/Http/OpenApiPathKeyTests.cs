using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that every key of the document's <c>paths</c> starts with a slash, whether the link that declares it wrote
/// one, and that a route declared with and without the slash is one path.
/// </summary>
/// <remarks>
/// Legacy keyed each path <c>"/" + route.RoutingUrl</c> (the URL format without its leading slash), so Signature365's
/// <c>[Link(..., "signatures/{SignatureId}/revisions/{Id}/publish")]</c> was <c>/signatures/...</c>. A path without the
/// slash is not a valid OpenAPI path, and a client generator that joins it to a base URL loses the separator.
/// </remarks>
public sealed class OpenApiPathKeyTests
{
    [Theory]
    [InlineData(OpenApiSchemaNaming.Default)]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    public void Every_path_starts_with_a_slash(OpenApiSchemaNaming naming)
    {
        using var document = Render(naming);

        document.RootElement.GetProperty("paths").EnumerateObject().Select(static path => path.Name)
            .Where(static name => !name.StartsWith('/'))
            .Should().BeEmpty("an OpenAPI path key starts with '/'");
    }

    [Theory]
    [InlineData(OpenApiSchemaNaming.Default)]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    public void A_route_declared_with_and_without_the_slash_is_one_path_with_both_operations(OpenApiSchemaNaming naming)
    {
        using var document = Render(naming);

        var paths = document.RootElement.GetProperty("paths");
        var matching = paths.EnumerateObject().Where(static path => path.Name.EndsWith("path-key/unslashed/{Id}", StringComparison.Ordinal)).ToList();

        matching.Should().ContainSingle();
        matching[0].Name.Should().Be("/path-key/unslashed/{Id}");

        var get = matching[0].Value.GetProperty("get").GetProperty("operationId").GetString();
        var post = matching[0].Value.GetProperty("post").GetProperty("operationId").GetString();

        get.Should().NotBeNullOrEmpty();
        post.Should().NotBeNullOrEmpty().And.NotBe(get);
    }

    [Fact]
    public void Under_legacy_naming_an_operation_at_two_paths_numbers_its_operation_ids_in_the_order_its_links_are_declared()
    {
        // Legacy reached an operation's paths in the order its links were declared, and numbered the second one's id, whether
        // the link that came first wrote its leading slash.
        using var document = Render(OpenApiSchemaNaming.Legacy);

        var paths = document.RootElement.GetProperty("paths");

        paths.GetProperty("/path-key/multi-z").GetProperty("get").GetProperty("operationId").GetString().Should().Be("GetPathKeyMulti");
        paths.GetProperty("/path-key/multi-a").GetProperty("get").GetProperty("operationId").GetString().Should().Be("GetPathKeyMulti2");
    }

    private static JsonDocument Render(OpenApiSchemaNaming naming)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(
            new IntegrationTestBlueprintApp(),
            new OpenApiInfoOptions { SchemaNaming = naming });

        return JsonDocument.Parse(rendered.Json);
    }
}

/// <summary>Reads at a route declared without its leading slash.</summary>
[AllowAnonymous]
[RootLink("path-key/unslashed/{Id}")]
public partial class GetPathKeyQuery : IQuery<string>
{
    public string Id { get; set; } = string.Empty;

    public static string Handle(GetPathKeyQuery query)
    {
        return query.Id;
    }
}

/// <summary>Posts at the same route declared with its leading slash.</summary>
[AllowAnonymous]
[RootLink("/path-key/unslashed/{Id}")]
[HttpPost]
public partial class SubmitPathKeyCommand : ICommand<string>
{
    public string Id { get; set; } = string.Empty;

    public static string Handle(SubmitPathKeyCommand command)
    {
        return command.Id;
    }
}

/// <summary>Reads at two routes, the first declared without its leading slash and sorting after the second.</summary>
[AllowAnonymous]
[RootLink("path-key/multi-z")]
[RootLink("/path-key/multi-a", Rel = "alternate")]
public partial class GetPathKeyMultiQuery : IQuery<string>
{
    public static string Handle(GetPathKeyMultiQuery query)
    {
        return string.Empty;
    }
}

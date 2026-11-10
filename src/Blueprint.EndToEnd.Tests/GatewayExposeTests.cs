using System.Net;
using System.Text.Json;
using Blueprint.EndToEnd.Tests.SampleSystem;

namespace Blueprint.EndToEnd.Tests;

/// <summary>
/// <c>[Expose&lt;TInner&gt;]</c> on real Kestrel (Symprex/blueprint#65): the sample gateway's reads are declared only as
/// <c>[Expose&lt;GetTemplateQuery&gt;]</c> and <c>[Expose&lt;GetTemplatesQuery&gt;]</c> partial classes, with no properties
/// and no handler of their own, and answer with the inner service's resource and the gateway's own links.
/// </summary>
[Collection(SampleSystemCollection.Name)]
public sealed class GatewayExposeTests(SampleSystemFixture system)
{
    [Fact]
    public async Task The_exposed_op_returns_the_inner_resource_with_gateway_links()
    {
        var id = await this.SeedTemplateAsync("Exposed Template");
        using var caller = system.Gateway.CreateClient(system.CreateCallerToken());

        var response = await caller.GetAsync($"gateway/templates/{id}");

        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        using var body = JsonDocument.Parse(text);

        // The inner resource, not a gateway-shaped copy: the sources are only on the inner TemplateApiResource.
        body.RootElement.GetProperty("id").GetString().Should().Be(id);
        body.RootElement.GetProperty("name").GetString().Should().Be("Exposed Template");
        body.RootElement.GetProperty("htmlSource").GetString().Should().Be("<p>Exposed Template</p>");

        // Links the gateway wrote from its own [SelfLink], never the inner service's.
        var links = body.RootElement.GetProperty("$links");
        links.GetProperty("self").GetProperty("href").GetString()
            .Should().Be(new Uri(system.Gateway.BaseAddress, $"gateway/templates/{id}").ToString());
        links.EnumerateObject().Select(static link => link.Value.GetProperty("href").GetString())
            .Should().OnlyContain(href => href!.StartsWith(system.Gateway.BaseAddress + "gateway/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_exposed_op_answers_the_inner_not_found_result_as_its_own_404()
    {
        using var caller = system.Gateway.CreateClient(system.CreateCallerToken());

        var response = await caller.GetAsync("gateway/templates/no-such-exposed-template");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_exposed_list_returns_the_inner_resources()
    {
        await this.SeedTemplateAsync("Exposed Listed Template");
        using var caller = system.Gateway.CreateClient(system.CreateCallerToken());

        var response = await caller.GetAsync("gateway/templates");

        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        using var body = JsonDocument.Parse(text);
        body.RootElement.EnumerateArray()
            .Should().Contain(static t => t.GetProperty("name").GetString() == "Exposed Listed Template"
                && t.GetProperty("htmlSource").GetString() == "<p>Exposed Listed Template</p>");
    }

    [Fact]
    public async Task An_exposed_ops_OpenAPI_parameters_equal_the_inner_ops()
    {
        using var gateway = system.Gateway.CreateClient(system.CreateCallerToken());
        using var inner = system.Inner.CreateClient(system.CreateCallerToken());

        var exposed = await FindParametersAsync(gateway, "gateway/.blueprint/openapi.json", "/templates/{id}");
        var original = await FindParametersAsync(inner, "api/.blueprint/openapi.json", "/templates/{id}");

        exposed.Should().NotBeEmpty();
        exposed.Should().Equal(original);
    }

    private static async Task<List<string>> FindParametersAsync(System.Net.Http.HttpClient client, string documentPath, string path)
    {
        var response = await client.GetAsync(documentPath);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);

        using var document = JsonDocument.Parse(text);
        var paths = document.RootElement.GetProperty("paths");
        var names = paths.EnumerateObject().Select(static p => p.Name).ToList();
        var name = names.Should().ContainSingle(n => string.Equals(n, path, StringComparison.OrdinalIgnoreCase), string.Join(", ", names)).Which;
        var operation = paths.GetProperty(name).GetProperty("get");

        return operation.TryGetProperty("parameters", out var parameters)
            ? parameters.EnumerateArray().Select(static p => p.GetRawText()).ToList()
            : [];
    }

    /// <summary>
    /// Creates a template directly in the inner app, over its own socket, and returns its id.
    /// </summary>
    private async Task<string> SeedTemplateAsync(string name)
    {
        using var inner = system.Inner.CreateClient(system.CreateCallerToken());
        var response = await inner.PostAsJsonAsync(
            "api/templates",
            new { name, htmlSource = $"<p>{name}</p>", plainTextSource = name });
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"seeding should succeed, but the inner app returned {response.StatusCode}: {body}");

        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").GetProperty("id").GetString()!;
    }
}

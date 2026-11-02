using System.Net;
using System.Text.Json;
using Blueprint.EndToEnd.Tests.SampleSystem;
using Blueprint.HttpClient;
using Blueprint.SourceGenerator.Sample;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using Blueprint.SourceGenerator.Sample.Gateway.Contract.Api;

namespace Blueprint.EndToEnd.Tests;

/// <summary>
/// A caller reaches the inner template service through the sample gateway, both apps on real Kestrel and
/// every hop over a real socket. Ported from the two-host <c>TestServer</c> <c>GatewayEndToEndTests</c>.
/// </summary>
[Collection(SampleSystemCollection.Name)]
public sealed class GatewayRoundTripTests(SampleSystemFixture system)
{
    [Fact]
    public async Task An_authenticated_gateway_call_reaches_the_inner_app_over_real_sockets_and_returns_its_resource()
    {
        var id = await this.SeedTemplateAsync("Round Trip Template");
        using var caller = system.Gateway.CreateClient(system.CreateCallerToken());

        var response = await caller.GetAsync($"gateway/templates/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var template = await response.Content.ReadFromJsonAsync<GatewayTemplateResource>();
        template!.Id.Should().Be(id);
        template.Name.Should().Be("Round Trip Template");
    }

    [Fact]
    public async Task A_gateway_call_without_a_token_does_not_reach_the_inner_resource()
    {
        // The inner app refuses an anonymous hop, so the round trip above only succeeds because the caller's
        // own token reached it.
        var id = await this.SeedTemplateAsync("Anonymous Caller Template");
        using var caller = system.Gateway.CreateClient();

        var response = await caller.GetAsync($"gateway/templates/{id}");

        response.IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task Typed_client_rethrows_the_ApiException_the_inner_handler_threw()
    {
        // The inner handler throws NotFoundException for an unknown id; the typed client must surface the same
        // problem, so a caller can match it by type and a gateway can pass it on.
        using var httpClient = system.Inner.CreateClient(system.CreateCallerToken());
        httpClient.BaseAddress = new Uri(system.Inner.BaseAddress, "api/");
        var client = new BlueprintHttpClient<SampleBlueprintApp>(httpClient, new SampleBlueprintApp());

        var act = () => client.SendAsync<JsonElement>(new UpdateTemplateCommand
        {
            Id = "missing",
            Name = "Renamed",
            HtmlSource = "<p>Renamed</p>",
        });

        var exception = (await act.Should().ThrowAsync<ApiException>()).Which;
        exception.Title.Should().Be("The requested resource could not be found");
        exception.Type.Should().Be("not_found");
        exception.HttpStatus.Should().Be(404);
        exception.Detail.Should().Be("Template 'missing' not found.");
    }

    [Fact]
    public async Task List_templates_returns_inner_data_through_gateway()
    {
        await this.SeedTemplateAsync("Listed Template");
        using var caller = system.Gateway.CreateClient(system.CreateCallerToken());

        var response = await caller.GetAsync("gateway/templates");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var templates = await response.Content.ReadFromJsonAsync<GatewayTemplateResource[]>();
        templates.Should().ContainSingle(static t => t.Name == "Listed Template");
    }

    [Fact]
    public async Task Get_template_returns_404_for_unknown_id()
    {
        using var caller = system.Gateway.CreateClient(system.CreateCallerToken());

        var response = await caller.GetAsync("gateway/templates/nonexistent-id");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_template_posts_to_inner_and_returns_resource()
    {
        using var caller = system.Gateway.CreateClient(system.CreateCallerToken());

        var response = await caller.PostAsJsonAsync(
            "gateway/templates",
            new { name = "Gateway Created", htmlSource = "<p>gw</p>", plainTextSource = "gw" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var resource = await response.Content.ReadFromJsonAsync<GatewayTemplateResource>();
        resource!.Name.Should().Be("Gateway Created");
        resource.IsArchived.Should().BeFalse();
    }

    [Fact]
    public async Task Create_template_with_invalid_input_returns_422_from_gateway_validation()
    {
        // An empty name violates [Required] and [StringLength(200, MinimumLength = 1)] on
        // CreateGatewayTemplateCommand, so the gateway refuses it before any hop.
        using var caller = system.Gateway.CreateClient(system.CreateCallerToken());

        var response = await caller.PostAsJsonAsync(
            "gateway/templates",
            new { name = string.Empty, htmlSource = "<p>x</p>", plainTextSource = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Create_then_get_through_gateway_returns_the_same_template()
    {
        using var caller = system.Gateway.CreateClient(system.CreateCallerToken());
        var createResponse = await caller.PostAsJsonAsync(
            "gateway/templates",
            new { name = "Created Then Fetched", htmlSource = "<p>rt</p>", plainTextSource = "rt" });
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK, await createResponse.Content.ReadAsStringAsync());
        var created = await createResponse.Content.ReadFromJsonAsync<GatewayTemplateResource>();

        var getResponse = await caller.GetAsync($"gateway/templates/{created!.Id}");

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResponse.Content.ReadFromJsonAsync<GatewayTemplateResource>();
        fetched!.Id.Should().Be(created.Id);
        fetched.Name.Should().Be("Created Then Fetched");
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

        // CreateTemplateCommand returns a resource event, which wraps the created resource in "data".
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").GetProperty("id").GetString()!;
    }
}

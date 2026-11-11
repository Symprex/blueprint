using System.Net;
using System.Text.Json;
using Blueprint.EndToEnd.Tests.SampleSystem;

namespace Blueprint.EndToEnd.Tests;

/// <summary>
/// <c>[TranslateError]</c> on a gateway operation (D8): an upstream problem of the named type is answered as the gateway's
/// own problem, across the real gateway-to-inner hop.
/// </summary>
[Collection(SampleSystemCollection.Name)]
public sealed class TranslateErrorTests(SampleSystemFixture system)
{
    [Fact]
    public async Task An_upstream_problem_named_by_TranslateError_is_answered_as_the_gateways_own_problem()
    {
        using var caller = system.NativeGateway.CreateClient(system.CreateCallerToken());

        var response = await caller.PutAsJsonAsync(
            "gateway/templates/no-such-template",
            new { name = "Renamed", htmlSource = "<p>Renamed</p>" });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, body);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("type").GetString().Should().Be("gateway_template_missing");
        problem.RootElement.GetProperty("title").GetString().Should().Be("Gateway template missing");
        problem.RootElement.GetProperty("detail").GetString().Should().Be("Template 'no-such-template' not found.");
    }

    [Fact]
    public async Task A_gateway_rename_forwards_every_content_field_so_the_plain_text_survives()
    {
        var id = await system.SeedTemplateAsync("Rename Source Template");
        using var caller = system.NativeGateway.CreateClient(system.CreateCallerToken());

        var response = await caller.PutAsJsonAsync(
            $"gateway/templates/{id}",
            new { name = "Renamed Template", htmlSource = "<p>Renamed</p>", plainTextSource = "Renamed plain text" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var inner = system.Inner.CreateClient(system.CreateCallerToken());
        using var stored = JsonDocument.Parse(await inner.GetStringAsync($"api/templates/{id}"));
        var template = stored.RootElement;
        template.GetProperty("name").GetString().Should().Be("Renamed Template");
        template.GetProperty("plainTextSource").GetString().Should().Be("Renamed plain text");
    }
}

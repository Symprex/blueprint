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
}

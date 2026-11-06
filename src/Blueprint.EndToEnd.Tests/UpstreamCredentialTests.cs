using System.Net;
using Blueprint.EndToEnd.Tests.SampleSystem;

namespace Blueprint.EndToEnd.Tests;

/// <summary>
/// The credential a gateway's forward sends the inner service, as the inner service receives it off the socket (D7): the
/// caller's own <c>Authorization</c> header by default, or what an operation's <c>[UpstreamCredential&lt;T&gt;]</c> strategy
/// creates in its place. Both run through <see cref="SampleSystemFixture.NativeGateway"/>, where the harness forwards
/// nothing on the gateway's behalf.
/// </summary>
[Collection(SampleSystemCollection.Name)]
public sealed class UpstreamCredentialTests(SampleSystemFixture system)
{
    [Fact]
    public async Task A_gateway_forward_sends_the_callers_own_Authorization_header()
    {
        var id = await system.SeedTemplateAsync("Caller Credential Template");
        var callerToken = system.CreateCallerToken();
        using var caller = system.NativeGateway.CreateClient(callerToken);

        var response = await caller.GetAsync($"gateway/templates/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        system.InnerRequests.LatestAuthorizationFor($"/api/templates/{id}").Should().Be($"Bearer {callerToken}");
    }

    [Fact]
    public async Task An_UpstreamCredential_strategy_overrides_the_callers_Authorization_header()
    {
        var id = await system.SeedTemplateAsync("Service Credential Template");
        using var caller = system.NativeGateway.CreateClient(system.CreateCallerToken());

        var response = await caller.GetAsync($"gateway/service/templates/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        system.InnerRequests.LatestAuthorizationFor($"/api/templates/{id}").Should().Be($"Bearer {system.ServiceToken}");
    }
}

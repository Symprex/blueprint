using System.Net;
using System.Security.Claims;
using Blueprint.EndToEnd.Tests.SampleSystem;

namespace Blueprint.EndToEnd.Tests.Harness;

/// <summary>
/// The harness's own contract: each app is on a real loopback socket of its own, the test issuer's tokens are
/// validated for real, and stopping an app frees its port.
/// </summary>
[Collection(SampleSystemCollection.Name)]
public sealed class MultiAppHostTests(SampleSystemFixture system)
{
    [Fact]
    public void Each_app_listens_on_its_own_loopback_port()
    {
        system.Inner.BaseAddress.Scheme.Should().Be(Uri.UriSchemeHttp);
        system.Inner.BaseAddress.IsLoopback.Should().BeTrue();
        system.Inner.BaseAddress.Port.Should().BePositive();

        system.Gateway.BaseAddress.IsLoopback.Should().BeTrue();
        system.Gateway.BaseAddress.Port.Should().BePositive().And.NotBe(system.Inner.BaseAddress.Port);
    }

    [Fact]
    public async Task Inner_app_accepts_a_token_from_the_test_issuer()
    {
        using var client = system.Inner.CreateClient(system.CreateCallerToken());

        var response = await client.GetAsync("api/templates");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Inner_app_refuses_a_call_with_no_token()
    {
        using var client = system.Inner.CreateClient();

        var response = await client.GetAsync("api/templates");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Inner_app_refuses_a_token_signed_by_another_issuer()
    {
        var forged = new TestJwtIssuer().CreateToken(
        [
            new Claim("sub", "e2e-user"),
            new Claim("scope", "template:read"),
        ]);
        using var client = system.Inner.CreateClient(forged);

        var response = await client.GetAsync("api/templates");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Stopping_an_app_closes_its_port()
    {
        await using var host = new MultiAppHost(new TestJwtIssuer());
        var app = await host.StartAsync<Blueprint.SourceGenerator.Sample.Startup>("inner");
        host["inner"].Should().BeSameAs(app);
        using var client = app.CreateClient();
        (await client.GetAsync("api/templates")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await app.StopAsync();

        var afterStop = () => client.GetAsync("api/templates");
        await afterStop.Should().ThrowAsync<HttpRequestException>();
    }
}

using System.Net;
using System.Text.Json;
using Blueprint.EndToEnd.Tests.SampleSystem;

namespace Blueprint.EndToEnd.Tests;

/// <summary>
/// A generated forward (<c>[Expose&lt;TInner&gt;]</c>) applies its operation's declared upstream credential and error
/// translations (Symprex/blueprint#69, D7, D8), across the real gateway-to-inner hop:
/// <c>GetGatewayTenantTemplateQuery</c> exchanges the caller's token for a tenant token through
/// <c>TenantTokenCredentialStrategy</c>, as Signature365's partner gateway does, and answers the inner service's
/// <c>not_found</c> as its own problem.
/// </summary>
/// <remarks>
/// The inner service's tenant-scoped query refuses a token whose <c>account_id</c> is not the tenant (<c>TenantIsolationApiAuthoriser</c>), so a success proves the exchange
/// reached it. The exchange is the fixture's <see cref="SampleSystemFixture.TenantTokens"/>, which mints a tenant token.
/// </remarks>
[Collection(SampleSystemCollection.Name)]
public sealed class GatewayCredentialTests(SampleSystemFixture system)
{
    private const string TenantId = "tenant-e2e";

    [Fact]
    public async Task A_generated_forward_sends_the_token_its_UpstreamCredential_strategy_exchanged_the_callers_for()
    {
        var id = await system.SeedTemplateAsync("Tenant Credential Template");
        var callerToken = system.CreateCallerToken();
        using var caller = system.NativeGateway.CreateClient(callerToken);

        var response = await caller.GetAsync($"gateway/tenants/{TenantId}/templates/{id}");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var resource = JsonDocument.Parse(body);
        resource.RootElement.GetProperty("id").GetString().Should().Be(id);

        var exchange = system.TenantTokens.ExchangeFor(TenantId);
        exchange.CallerAuthorization.Should().Be($"Bearer {callerToken}");
        system.InnerRequests.LatestAuthorizationFor($"/api/tenants/{TenantId}/templates/{id}").Should().Be($"Bearer {exchange.TenantToken}");
    }

    [Fact]
    public async Task The_inner_service_refuses_the_callers_own_token_for_a_tenant_query()
    {
        // The control for the test above: without the exchange, the inner service would not have answered.
        var id = await system.SeedTemplateAsync("Tenant Refusal Template");
        using var inner = system.Inner.CreateClient(system.CreateCallerToken());

        var response = await inner.GetAsync($"api/tenants/{TenantId}/templates/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_generated_forward_answers_an_upstream_problem_its_TranslateError_names_as_the_gateways_own()
    {
        using var caller = system.NativeGateway.CreateClient(system.CreateCallerToken());

        var response = await caller.GetAsync($"gateway/tenants/{TenantId}/templates/no-such-template");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, body);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("type").GetString().Should().Be("gateway_tenant_template_missing");
        problem.RootElement.GetProperty("title").GetString().Should().Be("Tenant template missing");
        problem.RootElement.GetProperty("detail").GetString().Should().Be($"Template 'no-such-template' not found for tenant '{TenantId}'.");
    }
}

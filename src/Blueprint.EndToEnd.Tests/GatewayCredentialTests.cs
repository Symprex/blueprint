using System.Net;
using System.Security.Claims;
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
/// <para>
/// The strategy's <c>TenantIdProperty</c> names the inner <c>TenantId</c>, which the gateway's
/// <c>[GatewayBind("TenantId", FromClaim = "tenant_id")]</c> binds (#66): the gateway operation has no <c>TenantId</c>, so
/// the strategy reads it from the inner operation the forward built.
/// </para>
/// <para>
/// The inner service's tenant-scoped query refuses a token whose <c>account_id</c> is not the tenant
/// (<c>TenantIsolationApiAuthoriser</c>), and the caller's own token has none, so a success proves the exchange reached
/// it. The exchange is the fixture's <see cref="SampleSystemFixture.TenantTokens"/>, which mints a tenant token.
/// </para>
/// </remarks>
[Collection(SampleSystemCollection.Name)]
public sealed class GatewayCredentialTests(SampleSystemFixture system)
{
    private const string TenantId = "tenant-e2e";

    [Fact]
    public async Task A_generated_forward_sends_the_token_its_UpstreamCredential_strategy_exchanged_for_the_bound_tenant()
    {
        var id = await system.SeedTemplateAsync("Tenant Credential Template");
        var callerToken = this.CreateTenantCallerToken();
        using var caller = system.NativeGateway.CreateClient(callerToken);

        var response = await caller.GetAsync($"gateway/tenants/current/templates/{id}");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var resource = JsonDocument.Parse(body);
        resource.RootElement.GetProperty("id").GetString().Should().Be(id);

        var exchange = system.TenantTokens.ExchangeFor(TenantId);
        exchange.CallerAuthorization.Should().Be($"Bearer {callerToken}");
        system.InnerRequests.LatestAuthorizationFor($"/api/tenants/{TenantId}/templates/{id}").Should().Be($"Bearer {exchange.TenantToken}");
    }

    [Fact]
    public async Task A_tenant_the_caller_sends_does_not_change_the_tenant_the_token_is_exchanged_for()
    {
        var id = await system.SeedTemplateAsync("Tenant Intruder Template");
        using var caller = system.NativeGateway.CreateClient(this.CreateTenantCallerToken());

        var response = await caller.GetAsync($"gateway/tenants/current/templates/{id}?tenantId=intruder");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var exchange = system.TenantTokens.ExchangeFor(TenantId);
        system.InnerRequests.LatestAuthorizationFor($"/api/tenants/{TenantId}/templates/{id}").Should().Be($"Bearer {exchange.TenantToken}");
        var exchangedForIntruder = () => system.TenantTokens.ExchangeFor("intruder");
        exchangedForIntruder.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task The_inner_service_refuses_the_callers_own_token_for_a_tenant_query()
    {
        // The control for the tests above: without the exchange, the inner service would not have answered.
        var id = await system.SeedTemplateAsync("Tenant Refusal Template");
        using var inner = system.Inner.CreateClient(this.CreateTenantCallerToken());

        var response = await inner.GetAsync($"api/tenants/{TenantId}/templates/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_inner_service_refuses_a_tenant_query_from_the_tenants_account_without_template_read()
    {
        // Tenant isolation checks only the account; the template read scope GetTemplateQuery requires still applies.
        var id = await system.SeedTemplateAsync("Tenant Unscoped Template");
        var token = system.Issuer.CreateToken([new Claim("sub", "e2e-tenant"), new Claim("account_id", TenantId)]);
        using var inner = system.Inner.CreateClient(token);

        var response = await inner.GetAsync($"api/tenants/{TenantId}/templates/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_generated_forward_answers_an_upstream_problem_its_TranslateError_names_as_the_gateways_own()
    {
        using var caller = system.NativeGateway.CreateClient(this.CreateTenantCallerToken());

        var response = await caller.GetAsync("gateway/tenants/current/templates/no-such-template");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, body);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("type").GetString().Should().Be("gateway_tenant_template_missing");
        problem.RootElement.GetProperty("title").GetString().Should().Be("Tenant template missing");
        problem.RootElement.GetProperty("detail").GetString().Should().Be($"Template 'no-such-template' not found for tenant '{TenantId}'.");
    }

    /// <summary>
    /// A caller of the tenant: its <c>tenant_id</c> claim is what the gateway binds, and it has no <c>account_id</c>, so
    /// the inner service would refuse its token.
    /// </summary>
    private string CreateTenantCallerToken()
    {
        return system.Issuer.CreateToken(
            [new Claim("sub", "e2e-partner"), new Claim("tenant_id", TenantId), .. SampleSystemFixture.TemplateScopes.Select(static s => new Claim("scope", s))]);
    }
}

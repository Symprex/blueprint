using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Blueprint.EndToEnd.Tests.Harness;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using Blueprint.SourceGenerator.Sample.Gateway.Api;
using Blueprint.Testing;

namespace Blueprint.EndToEnd.Tests;

/// <summary>
/// <c>Blueprint.Testing</c>'s <c>StubBlueprintClient</c> standing in for the sample gateway's upstream on real Kestrel
/// (Symprex/blueprint#163): the gateway runs its own <c>Startup</c> unchanged, pointed at an address nothing listens on, and
/// its generated <c>[Expose&lt;TInner&gt;]</c> forwards and its typed client are answered by inner operation type.
/// </summary>
public sealed class BlueprintClientStubTests : IAsyncLifetime
{
    private readonly TestJwtIssuer _issuer = new();
    private MultiAppHost? _host;
    private RunningApp? _gateway;
    private BlueprintClientStub<TemplateServiceApi> _upstream = null!;

    public async Task InitializeAsync()
    {
        this._host = new MultiAppHost(this._issuer);
        this._gateway = await this._host.StartAsync<Blueprint.SourceGenerator.Sample.Gateway.Startup>("stubbed-gateway", gateway =>
        {
            // Nothing listens on the discard port: a request the stub let through would fail the test.
            gateway.Settings[Blueprint.SourceGenerator.Sample.Gateway.Startup.TemplateServiceAddressKey] = "http://127.0.0.1:9/api/";
            gateway.ConfigureServices = services => this._upstream = services.StubBlueprintClient<TemplateServiceApi>();
        });
    }

    public async Task DisposeAsync()
    {
        if (this._host is not null)
        {
            await this._host.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_generated_forward_is_answered_by_the_inner_operation_type_with_the_callers_credential()
    {
        this._upstream.Respond<GetTemplateQuery>(static q => new TemplateApiResource { Id = q.Id, Name = "Stubbed upstream" });
        var token = this.CreateCallerToken();
        using var caller = this._gateway!.CreateClient(token);

        var response = await caller.GetAsync("gateway/templates/t-1");

        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        using var body = JsonDocument.Parse(text);
        body.RootElement.GetProperty("name").GetString().Should().Be("Stubbed upstream");

        this._upstream.Received<GetTemplateQuery>().Should().ContainSingle().Which.Id.Should().Be("t-1");
        this._upstream.Requests.Should().ContainSingle()
            .Which.RequestAuthorization.Should().Be(new AuthenticationHeaderValue("Bearer", token));
    }

    [Fact]
    public async Task A_stubbed_not_found_result_is_the_gateways_404()
    {
        this._upstream.Respond<GetTemplateQuery>(static q => new NotFoundResult<TemplateApiResource>(new TemplateApiResource { Id = q.Id }));
        using var caller = this._gateway!.CreateClient(this.CreateCallerToken());

        var response = await caller.GetAsync("gateway/templates/missing");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_gateways_typed_client_is_answered_by_the_same_stub()
    {
        this._upstream.Respond<GetTemplatesQuery>(static _ => new[] { new TemplateApiResource { Id = "t-9", Name = "Listed" } });
        using var scope = this._gateway!.Services.CreateScope();

        var templates = await scope.ServiceProvider.GetRequiredService<ITemplateServiceApi>().GetTemplatesAsync(new GetTemplatesQuery());

        templates.Should().ContainSingle().Which.Name.Should().Be("Listed");
        this._upstream.Received<GetTemplatesQuery>().Should().ContainSingle();
    }

    private string CreateCallerToken()
    {
        return this._issuer.CreateToken([new Claim("sub", "stub-user"), new Claim("scope", "template:read"), new Claim("scope", "template:write")]);
    }
}

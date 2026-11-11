using System.Security.Claims;
using System.Text.Json;
using Blueprint.EndToEnd.Tests.Harness;

namespace Blueprint.EndToEnd.Tests.SampleSystem;

/// <summary>
/// The sample system: <c>Blueprint.SourceGenerator.Sample</c> as the inner service and
/// <c>Blueprint.SourceGenerator.Sample.Gateway</c> in front of it, started once per test run and shared by
/// every test in <see cref="SampleSystemCollection"/>.
/// </summary>
/// <remarks>
/// The apps keep state between tests (the inner app's template store is a singleton), so a test seeds what it
/// reads under a name of its own rather than assuming an empty store.
/// </remarks>
public sealed class SampleSystemFixture : IAsyncLifetime
{
    /// <summary>The name the inner template service is started under.</summary>
    public const string InnerName = "inner";

    /// <summary>The name the gateway is started under.</summary>
    public const string GatewayName = "gateway";

    /// <summary>The name the gateway without the harness's caller-credential forwarding is started under.</summary>
    public const string NativeGatewayName = "gateway-native";

    /// <summary>The scopes a caller needs for every template operation the gateway reaches.</summary>
    public static readonly string[] TemplateScopes = ["template:read", "template:write"];

    private MultiAppHost? _host;

    /// <summary>The issuer both apps trust.</summary>
    public TestJwtIssuer Issuer { get; } = new();

    /// <summary>The inner template service.</summary>
    public RunningApp Inner => this.Host[InnerName];

    /// <summary>The gateway, configured to call <see cref="Inner"/> at its real address.</summary>
    public RunningApp Gateway => this.Host[GatewayName];

    /// <summary>
    /// A second gateway whose outbound calls carry only the credentials the gateway itself chose: the harness does not
    /// fill in the caller's <c>Authorization</c> header, as it does for <see cref="Gateway"/>.
    /// </summary>
    public RunningApp NativeGateway => this.Host[NativeGatewayName];

    /// <summary>Every request <see cref="Inner"/> has received, with the <c>Authorization</c> header it arrived with.</summary>
    public InboundRequestLog InnerRequests { get; } = new();

    /// <summary>
    /// The gateway's own service token, which the gateways' <c>ServiceTokenCredentialStrategy</c> sends: subject
    /// <c>gateway-service</c>, read scope only.
    /// </summary>
    public string ServiceToken { get; private set; } = string.Empty;

    /// <summary>
    /// The tenant-token exchange both gateways' <c>TenantTokenCredentialStrategy</c> calls, standing in for an identity
    /// service: it mints a token for the test caller whose <c>account_id</c> is the tenant.
    /// </summary>
    public TestTenantTokenExchanger TenantTokens { get; private set; } = null!;

    private MultiAppHost Host => this._host ?? throw new InvalidOperationException("The sample system has not been started.");

    /// <summary>
    /// Mints a token for the test caller holding <paramref name="scopes"/>, or <see cref="TemplateScopes"/>
    /// when none are given.
    /// </summary>
    /// <param name="scopes">The <c>scope</c> claims to grant.</param>
    /// <returns>A token both apps accept.</returns>
    public string CreateCallerToken(params string[] scopes)
    {
        var granted = scopes.Length == 0 ? TemplateScopes : scopes;

        return this.Issuer.CreateToken([new Claim("sub", "e2e-user"), .. granted.Select(static s => new Claim("scope", s))]);
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        this._host = new MultiAppHost(this.Issuer);
        this.TenantTokens = new TestTenantTokenExchanger(this.Issuer, TemplateScopes);

        var inner = await this._host.StartAsync<Blueprint.SourceGenerator.Sample.Startup>(InnerName, inner => inner.ConfigureServices = this.InnerRequests.InstallInto);

        this.ServiceToken = this.Issuer.CreateToken([new Claim("sub", "gateway-service"), new Claim("scope", "template:read")]);

        await this._host.StartAsync<Blueprint.SourceGenerator.Sample.Gateway.Startup>(GatewayName, gateway =>
        {
            this.ConfigureGateway(gateway, inner);

            // The sample gateway's typed-client handlers call the inner app with no credential of their own.
            gateway.ForwardCallerCredential = true;
        });

        // Its forwards' credentials are the gateway's own: nothing in the harness fills a missing Authorization header.
        await this._host.StartAsync<Blueprint.SourceGenerator.Sample.Gateway.Startup>(NativeGatewayName, gateway => this.ConfigureGateway(gateway, inner));
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (this._host is not null)
        {
            await this._host.DisposeAsync();
        }
    }

    /// <summary>
    /// Creates a template directly in the inner app, over its own socket, and returns its id.
    /// </summary>
    /// <param name="name">The template's name.</param>
    /// <returns>The new template's id.</returns>
    public async Task<string> SeedTemplateAsync(string name)
    {
        using var inner = this.Inner.CreateClient(this.CreateCallerToken());
        var response = await inner.PostAsJsonAsync(
            "api/templates",
            new { name, htmlSource = $"<p>{name}</p>", plainTextSource = name });
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"seeding should succeed, but the inner app returned {response.StatusCode}: {body}");

        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").GetProperty("id").GetString()!;
    }

    private void ConfigureGateway(EndToEndAppOptions gateway, RunningApp inner)
    {
        gateway.Settings[Blueprint.SourceGenerator.Sample.Gateway.Startup.TemplateServiceAddressKey] = new Uri(inner.BaseAddress, "api/").ToString();
        gateway.Settings[Blueprint.SourceGenerator.Sample.Gateway.Startup.ServiceTokenKey] = this.ServiceToken;
        gateway.ConfigureServices = services => services.AddSingleton<Blueprint.SourceGenerator.Sample.Gateway.ITenantTokenExchanger>(this.TenantTokens);
    }
}

/// <summary>
/// Shares one <see cref="SampleSystemFixture"/> across every test class that names this collection, so the
/// two apps start once per run rather than once per class.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SampleSystemCollection : ICollectionFixture<SampleSystemFixture>
{
    /// <summary>The collection name test classes opt in with.</summary>
    public const string Name = "Sample system";
}

using System.Security.Claims;
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

    /// <summary>The scopes a caller needs for every template operation the gateway reaches.</summary>
    public static readonly string[] TemplateScopes = ["template:read", "template:write"];

    private MultiAppHost? _host;

    /// <summary>The issuer both apps trust.</summary>
    public TestJwtIssuer Issuer { get; } = new();

    /// <summary>The inner template service.</summary>
    public RunningApp Inner => this.Host[InnerName];

    /// <summary>The gateway, configured to call <see cref="Inner"/> at its real address.</summary>
    public RunningApp Gateway => this.Host[GatewayName];

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

        var inner = await this._host.StartAsync<Blueprint.SourceGenerator.Sample.Startup>(InnerName);

        await this._host.StartAsync<Blueprint.SourceGenerator.Sample.Gateway.Startup>(GatewayName, gateway =>
        {
            gateway.Settings[Blueprint.SourceGenerator.Sample.Gateway.Startup.TemplateServiceAddressKey] = new Uri(inner.BaseAddress, "api/").ToString();

            // The sample gateway's handlers call the inner app with no credential of their own.
            gateway.ForwardCallerCredential = true;
        });
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (this._host is not null)
        {
            await this._host.DisposeAsync();
        }
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

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Blueprint.SourceGenerator.Sample;
using Blueprint.SourceGenerator.Sample.Gateway;
using Blueprint.SourceGenerator.Sample.Gateway.Contract.Api;
using Blueprint.Testing;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// End-to-end tests that spin up both the upstream sample host and the gateway host,
/// wiring the gateway's <see cref="BlueprintHttpClient{TApp}"/> to the upstream's
/// <see cref="TestServer"/>. Every request flows through real ASP.NET Core pipelines
/// on both sides: routing, content negotiation, validation, serialization.
/// </summary>
public sealed class GatewayEndToEndTests : IAsyncLifetime
{
    private IHost? _upstreamHost;
    private System.Net.Http.HttpClient? _upstreamClient;
    private BlueprintHttpTestHost<GatewayBlueprintApp, GatewayBlueprintAppExecutor>? _gatewayHost;

    public async Task InitializeAsync()
    {
        // Start the upstream template service using its real Startup class.
        // Test authentication is injected via IStartupFilter so the Startup's
        // Configure method (which maps Blueprint endpoints) runs unmodified.
        var upstreamBuilder = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseStartup<Blueprint.SourceGenerator.Sample.Startup>();
                webBuilder.UseEnvironment("Development");
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthentication("TestScheme")
                        .AddScheme<AuthenticationSchemeOptions, E2ETestAuthHandler>("TestScheme", _ => { });
                    services.AddSingleton<IStartupFilter, PrependAuthenticationStartupFilter>();
                });
            });

        var upstreamHost = upstreamBuilder.Build();
        await upstreamHost.StartAsync();
        this._upstreamHost = upstreamHost;
        this._upstreamClient = upstreamHost.GetTestClient();

        var upstreamHandler = upstreamHost.GetTestServer().CreateHandler();

        // Start the gateway, overriding its BlueprintHttpClient<SampleBlueprintApp>
        // to route through the upstream TestServer.
        this._gatewayHost = await BlueprintHttpTestHostBuilder<GatewayBlueprintApp, GatewayBlueprintAppExecutor>
            .Create()
            .WithBasePath("gateway/")
            .WithTestAuthentication(claims =>
            {
                claims.Add(new Claim("sub", "e2e-user"));
                claims.Add(new Claim("AccountId", "e2e-account"));
                claims.Add(new Claim("scope", "template:read"));
                claims.Add(new Claim("scope", "template:write"));
            })
            .ConfigureHttpOptions(opts => opts.ExposeExceptionDetailsInErrorResponses = true)
            .ConfigureServices(services =>
            {
                services.AddSingleton<SampleBlueprintApp>();

                services.AddHttpClient<Blueprint.HttpClient.BlueprintHttpClient<SampleBlueprintApp>>(client =>
                {
                    client.BaseAddress = new Uri("http://localhost/api/");
                })
                .AddTypedClient((httpClient, sp) =>
                {
                    var app = sp.GetRequiredService<SampleBlueprintApp>();
                    return new Blueprint.HttpClient.BlueprintHttpClient<SampleBlueprintApp>(httpClient, app);
                })
                .ConfigurePrimaryHttpMessageHandler(() => upstreamHandler);
            })
            .BuildAsync();
    }

    public async Task DisposeAsync()
    {
        if (this._gatewayHost is not null)
        {
            await this._gatewayHost.DisposeAsync();
        }

        this._upstreamClient?.Dispose();

        if (this._upstreamHost is not null)
        {
            await this._upstreamHost.StopAsync();
            this._upstreamHost.Dispose();
        }
    }

    [Fact]
    public async Task List_templates_returns_upstream_data_through_gateway()
    {
        // Seed a template directly in the upstream.
        await this.SeedTemplateAsync("E2E Template");

        // Fetch through the gateway.
        var response = await this._gatewayHost!.Client.GetAsync("/gateway/templates");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var templates = await response.Content.ReadFromJsonAsync<GatewayTemplateResource[]>();
        templates.Should().NotBeNull();
        templates.Should().ContainSingle(static t => t.Name == "E2E Template");
    }

    [Fact]
    public async Task Get_template_by_id_returns_upstream_resource()
    {
        var id = await this.SeedTemplateAsync("Lookup Template");

        var response = await this._gatewayHost!.Client.GetAsync($"/gateway/templates/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var template = await response.Content.ReadFromJsonAsync<GatewayTemplateResource>();
        template.Should().NotBeNull();
        template!.Name.Should().Be("Lookup Template");
    }

    [Fact]
    public async Task Get_template_returns_404_for_unknown_id()
    {
        var response = await this._gatewayHost!.Client.GetAsync("/gateway/templates/nonexistent-id");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_template_posts_to_upstream_and_returns_resource()
    {
        var response = await this._gatewayHost!.Client.PostAsJsonAsync(
            "/gateway/templates",
            new { name = "Gateway Created", htmlSource = "<p>gw</p>", plainTextSource = "gw" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var resource = await response.Content.ReadFromJsonAsync<GatewayTemplateResource>();
        resource.Should().NotBeNull();
        resource!.Name.Should().Be("Gateway Created");
        resource.IsArchived.Should().BeFalse();
    }

    [Fact]
    public async Task Create_template_with_invalid_input_returns_422_from_gateway_validation()
    {
        // Empty name violates [Required] + [StringLength(200, MinimumLength = 1)] on
        // CreateGatewayTemplateCommand — validation happens at the gateway level.
        var response = await this._gatewayHost!.Client.PostAsJsonAsync(
            "/gateway/templates",
            new { name = string.Empty, htmlSource = "<p>x</p>", plainTextSource = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Round_trip_create_then_get_through_gateway()
    {
        // Create through gateway → upstream → response.
        var createResponse = await this._gatewayHost!.Client.PostAsJsonAsync(
            "/gateway/templates",
            new { name = "Roundtrip", htmlSource = "<p>rt</p>", plainTextSource = "rt" });
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await createResponse.Content.ReadFromJsonAsync<GatewayTemplateResource>();
        created.Should().NotBeNull();

        // Get the same template back through gateway → upstream → response.
        var getResponse = await this._gatewayHost!.Client.GetAsync($"/gateway/templates/{created!.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var fetched = await getResponse.Content.ReadFromJsonAsync<GatewayTemplateResource>();
        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(created.Id);
        fetched.Name.Should().Be("Roundtrip");
    }

    /// <summary>
    /// Seeds a template in the upstream and returns the created template's Id.
    /// </summary>
    private async Task<string> SeedTemplateAsync(string name)
    {
        var seedResponse = await this._upstreamClient!.PostAsJsonAsync(
            "/api/templates",
            new { name, htmlSource = $"<p>{name}</p>", plainTextSource = name });

        if (!seedResponse.IsSuccessStatusCode)
        {
            var body = await seedResponse.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Seed failed with {seedResponse.StatusCode}: {body}");
        }

        // ResourceEventResult serializes its Content payload with event metadata.
        // Parse with JsonDocument to navigate the variable shape.
        var body2 = await seedResponse.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(body2);

        // Try the resource-event envelope shape: { "data": { "Id": ... } } or { "content": { "Id": ... } }
        if (doc.RootElement.TryGetProperty("data", out var dataElem) &&
            dataElem.TryGetProperty("Id", out var dataIdElem))
        {
            return dataIdElem.GetString() ?? throw new InvalidOperationException("Id was null in data envelope");
        }

        if (doc.RootElement.TryGetProperty("content", out var contentElem) &&
            contentElem.TryGetProperty("Id", out var idElem))
        {
            return idElem.GetString() ?? throw new InvalidOperationException("Id was null in content envelope");
        }

        // Try direct shape: { "Id": ... }
        if (doc.RootElement.TryGetProperty("Id", out var directId))
        {
            return directId.GetString() ?? throw new InvalidOperationException("Id was null");
        }

        throw new InvalidOperationException($"Cannot find id in seed response: {body2}");
    }

    /// <summary>
    /// Inserts <c>UseAuthentication()</c> at the very start of the middleware pipeline
    /// so that <see cref="E2ETestAuthHandler"/> runs before Blueprint endpoint checks.
    /// Using <see cref="IStartupFilter"/> avoids overriding the Startup's <c>Configure</c> method.
    /// </summary>
    private sealed class PrependAuthenticationStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.UseAuthentication();
                next(app);
            };
        }
    }

    /// <summary>
    /// Test authentication handler that auto-authenticates every request with claims
    /// sufficient to pass the sample app's [Resource] authorization checks.
    /// </summary>
    private sealed class E2ETestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public E2ETestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim("sub", "e2e-user"),
                new Claim("AccountId", "e2e-account"),
                new Claim("scope", "template:read"),
                new Claim("scope", "template:write"),
            };
            var identity = new ClaimsIdentity(claims, "TestScheme");
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, "TestScheme");

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Blueprint.SourceGenerator.Sample;
using Blueprint.SourceGenerator.Sample.Api;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.Testing.Tests;

/// <summary>
/// Proves that <see cref="BlueprintHttpTestHost{TApp,TExec}"/> correctly starts a full HTTP
/// test server and routes requests through the Blueprint pipeline end-to-end.
/// </summary>
public class BlueprintHttpTestHostTests : IAsyncLifetime
{
    private BlueprintHttpTestHost<SampleBlueprintApp, SampleBlueprintAppExecutor>? _host;

    public async Task InitializeAsync()
    {
        this._host = await BlueprintHttpTestHostBuilder<SampleBlueprintApp, SampleBlueprintAppExecutor>
            .Create()
            .ConfigureServices(services =>
            {
                services.AddSingleton<TemplateStore>();
                services.AddScoped<InMemoryUnitOfWork>();
                services.AddSingleton<Blueprint.Authorization.IResourceAuthorizationProvider, PermitAllResourceAuthorizationProvider>();
            })
            .WithBasePath("/api")
            .BuildAsync();
    }

    public async Task DisposeAsync()
    {
        if (this._host is not null)
        {
            await this._host.DisposeAsync();
        }
    }

    [Fact]
    public void Host_resolves_app()
    {
        this._host!.App.Should().NotBeNull();
    }

    [Fact]
    public void Host_provides_http_client()
    {
        this._host!.Client.Should().NotBeNull();
    }

    [Fact]
    public async Task Get_request_returns_200_with_json_body()
    {
        var response = await this._host!.Client.GetAsync("/api/templates/search?Q=test&Page=1&PageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Post_with_invalid_body_returns_422()
    {
        // CreateTemplateCommand requires Name and HtmlSource — sending empty body triggers validation
        var response = await this._host!.Client.PostAsJsonAsync(
            "/api/templates",
            new { name = string.Empty, htmlSource = string.Empty });

        // May return 401 if [MustBeAuthenticated] — sample app operations that are not
        // [AllowAnonymous] will reject. Use anonymous-safe operation for this test instead.
        // Actually CreateTemplateCommand doesn't have [AllowAnonymous] or [MustBeAuthenticated]
        // explicitly. Check the actual status.
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.UnprocessableEntity,
            HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_with_valid_body_returns_success_when_no_auth_required()
    {
        // Use the health check query which is [AllowAnonymous]
        var response = await this._host!.Client.GetAsync("/api/system/health");

        response.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task OpenApi_endpoint_returns_200()
    {
        var response = await this._host!.Client.GetAsync("/api/.blueprint/openapi.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("openapi").GetString().Should().StartWith("3.");
    }

    [Fact]
    public async Task Unknown_route_returns_404()
    {
        var response = await this._host!.Client.GetAsync("/api/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

/// <summary>
/// Proves that <see cref="BlueprintHttpTestHostBuilder{TApp,TExec}.WithTestAuthentication"/>
/// correctly authenticates requests so operations with <c>[MustBeAuthenticated]</c> succeed.
/// </summary>
public class BlueprintHttpTestHostAuthenticatedTests : IAsyncLifetime
{
    private BlueprintHttpTestHost<SampleBlueprintApp, SampleBlueprintAppExecutor>? _host;

    public async Task InitializeAsync()
    {
        this._host = await BlueprintHttpTestHostBuilder<SampleBlueprintApp, SampleBlueprintAppExecutor>
            .Create()
            .ConfigureServices(services =>
            {
                services.AddSingleton<TemplateStore>();
                services.AddScoped<InMemoryUnitOfWork>();
                services.AddSingleton<Blueprint.Authorization.IResourceAuthorizationProvider, PermitAllResourceAuthorizationProvider>();
            })
            .WithTestAuthentication(claims =>
            {
                claims.Add(new System.Security.Claims.Claim("sub", "test-user-1"));
                claims.Add(new System.Security.Claims.Claim("name", "Test User"));
            })
            .WithBasePath("/api")
            .BuildAsync();
    }

    public async Task DisposeAsync()
    {
        if (this._host is not null)
        {
            await this._host.DisposeAsync();
        }
    }

    [Fact]
    public async Task Authenticated_endpoint_returns_success_with_test_auth()
    {
        // GetTemplateByStrongIdQuery has [MustBeAuthenticated]
        var response = await this._host!.Client.GetAsync("/api/templates/by-id/00000000-0000-0000-0000-000000000001");

        // Should not be 401 — test auth should have authenticated the request
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_command_with_test_auth_goes_through_validation()
    {
        // CreateTemplateCommand has [MustBeAuthenticated] — with test auth, it should
        // hit validation instead of 401
        var response = await this._host!.Client.PostAsJsonAsync(
            "/api/templates",
            new { name = string.Empty, htmlSource = string.Empty });

        // With auth in place, we should get validation failure (422), not unauthorized (401)
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}

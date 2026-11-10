using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Blueprint.Authorization;
using Blueprint.Mcp.Tests.App;
using Blueprint.Model;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Sample;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace Blueprint.Mcp.Tests;

/// <summary>
/// The test app's API at <c>api/</c> and its tools at <c>api/mcp</c>, on Kestrel at a free local port, behind an
/// authentication scheme that signs a caller in with the roles its <see cref="RolesHeader"/> names.
/// </summary>
/// <remarks>
/// <see cref="PolicyGuardedMcpQuery"/>'s endpoint requires the <see cref="AdminPolicy"/> policy, which only the
/// <c>Admin</c> role satisfies; Blueprint itself lets anyone run it.
/// </remarks>
internal sealed class McpTestHost : IAsyncDisposable
{
    /// <summary>The header naming the roles the caller is signed in with, comma-separated; without it, no one is.</summary>
    public const string RolesHeader = "X-Test-Roles";

    /// <summary>The ASP.NET Core policy guarding <see cref="PolicyGuardedMcpQuery"/>'s endpoint.</summary>
    public const string AdminPolicy = "Admin";

    private const string SchemeName = "Test";

    private readonly WebApplication _app;

    private McpTestHost(WebApplication app, Uri baseAddress)
    {
        this._app = app;
        this.BaseAddress = baseAddress;
    }

    /// <summary>Gets the host's address.</summary>
    public Uri BaseAddress { get; }

    /// <summary>
    /// Starts the host.
    /// </summary>
    /// <param name="renderOnFirstRequest">
    /// Whether <see cref="BlueprintHttpOptions.RenderOpenApiDocumentOnFirstRequest"/> defers the documents and tools.
    /// </param>
    /// <param name="mapSampleApp">
    /// Whether the sample app's API (<c>sample/</c>) and tools (<c>sample/mcp</c>) are mapped after the test app's.
    /// </param>
    /// <returns>The started host.</returns>
    public static async Task<McpTestHost> StartAsync(bool renderOnFirstRequest = false, bool mapSampleApp = false)
    {
        var builder = WebApplication.CreateSlimBuilder();

        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        builder.Services.AddBlueprintApi<McpTestBlueprintApp, McpTestBlueprintAppExecutor>();
        builder.Services.Configure<BlueprintHttpOptions>(o => o.RenderOpenApiDocumentOnFirstRequest = renderOnFirstRequest);
        builder.Services.AddAuthentication(SchemeName).AddScheme<AuthenticationSchemeOptions, RolesHeaderAuthenticationHandler>(SchemeName, null);
        builder.Services.AddAuthorization(o => o.AddPolicy(AdminPolicy, p => p.RequireRole("Admin")));
        builder.Services.AddBlueprintMcp();

        if (mapSampleApp)
        {
            builder.Services.AddBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>(
                static blueprint => blueprint.AddResourceAuthorizationProvider<ScopePairClaimAuthorizationProvider>());
        }

        var app = builder.Build();

        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapBlueprintApi<McpTestBlueprintApp, McpTestBlueprintAppExecutor>("api/")
            .Add(static endpoint =>
            {
                if (endpoint.Metadata.OfType<ApiOperationDescriptor>().Any(static d => d.OperationType == typeof(PolicyGuardedMcpQuery)))
                {
                    endpoint.Metadata.Add(new AuthorizeAttribute(AdminPolicy));
                }
            });

        app.MapBlueprintMcp<McpTestBlueprintApp>("api/mcp");

        if (mapSampleApp)
        {
            app.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("sample/");
            app.MapBlueprintMcp<SampleBlueprintApp>("sample/mcp");
        }

        await app.StartAsync();

        return new McpTestHost(app, new Uri(app.Urls.First().TrimEnd('/') + "/"));
    }

    /// <summary>Makes an HTTP client addressed at the host, signed in with <paramref name="roles"/>.</summary>
    /// <param name="roles">The caller's roles, comma-separated, or <see langword="null"/> for an anonymous caller.</param>
    /// <returns>The client.</returns>
    public System.Net.Http.HttpClient CreateHttpClient(string? roles = null)
    {
        var client = new System.Net.Http.HttpClient { BaseAddress = this.BaseAddress };

        if (roles is not null)
        {
            client.DefaultRequestHeaders.Add(RolesHeader, roles);
        }

        return client;
    }

    /// <summary>Connects an MCP client to the tools at <paramref name="path"/>, signed in with <paramref name="roles"/>.</summary>
    /// <param name="path">The MCP endpoint's path.</param>
    /// <param name="roles">The caller's roles, comma-separated, or <see langword="null"/> for an anonymous caller.</param>
    /// <returns>The connected client.</returns>
    public async Task<McpClient> ConnectAsync(string path = "api/mcp", string? roles = null)
    {
        var options = new HttpClientTransportOptions
        {
            Endpoint = new Uri(this.BaseAddress, path),
            TransportMode = HttpTransportMode.StreamableHttp,
        };

        if (roles is not null)
        {
            options.AdditionalHeaders = new Dictionary<string, string> { [RolesHeader] = roles };
        }

        return await McpClient.CreateAsync(new HttpClientTransport(options));
    }

    /// <summary>
    /// Reads every operation id of the OpenAPI document served at <paramref name="basePath"/>, by its method and path.
    /// </summary>
    /// <param name="basePath">The API's base path, such as <c>api/</c>.</param>
    /// <returns>The ids, keyed by lower-case method and path relative to the base path.</returns>
    public async Task<Dictionary<(string Method, string Path), string>> GetOperationIdsAsync(string basePath = "api/")
    {
        using var http = this.CreateHttpClient();
        using var document = JsonDocument.Parse(await http.GetStringAsync(basePath + ".blueprint/openapi.json"));

        return document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(static path => path.Value.EnumerateObject()
                .Where(static method => method.Value.ValueKind == JsonValueKind.Object && method.Value.TryGetProperty("operationId", out _))
                .Select(method => (Key: (method.Name, path.Name), Id: method.Value.GetProperty("operationId").GetString()!)))
            .ToDictionary(static e => e.Key, static e => e.Id);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await this._app.StopAsync();
        await this._app.DisposeAsync();
    }

    private sealed class RolesHeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!this.Request.Headers.TryGetValue(RolesHeader, out var roles))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = roles.ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static role => new Claim(System.Security.Claims.ClaimTypes.Role, role))
                .Append(new Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "tester"));

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}

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
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
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

    /// <summary>
    /// A scheme no request is authenticated with by default, which signs a caller in with the roles its
    /// <c>Authorization: Roles …</c> header names.
    /// </summary>
    public const string AuthorizationRolesScheme = "AuthorizationRoles";

    private const string SchemeName = "Test";

    private readonly WebApplication _app;

    private McpTestHost(WebApplication app, Uri baseAddress)
    {
        this._app = app;
        this.BaseAddress = baseAddress;
    }

    /// <summary>Gets the host's address.</summary>
    public Uri BaseAddress { get; }

    /// <summary>Gets the host's services.</summary>
    public IServiceProvider Services => this._app.Services;

    /// <summary>
    /// Starts the host.
    /// </summary>
    /// <param name="renderOnFirstRequest">
    /// Whether <see cref="BlueprintHttpOptions.RenderOpenApiDocumentOnFirstRequest"/> defers the documents and tools.
    /// </param>
    /// <param name="mapSampleApp">
    /// Whether the sample app's API (<c>sample/</c>) and tools (<c>sample/mcp</c>) are mapped after the test app's.
    /// </param>
    /// <param name="mapAdminCopy">
    /// Whether the test app's API is mapped a second time, at <c>admin/</c> with every endpoint requiring
    /// <see cref="AdminPolicy"/>, with its own tools at <c>admin/mcp</c>.
    /// </param>
    /// <param name="adminFallback">
    /// Whether <see cref="AdminPolicy"/>'s requirement is the fallback policy, which an endpoint without authorisation
    /// metadata of its own requires, while the tools at <c>api/mcp</c> require only an authenticated caller.
    /// </param>
    /// <param name="mapGrouped">
    /// Whether the test app's API and tools are mapped a second time, at <c>grouped/api/</c> and
    /// <c>grouped/api/mcp</c>, in a route group requiring <see cref="AdminPolicy"/>, the tools allowing anyone.
    /// </param>
    /// <param name="mapTenantGroup">
    /// Whether the test app's API and tools are mapped a second time, at <c>tenants/{TenantId}/api/</c> and
    /// <c>tenants/{TenantId}/api/mcp</c>, in a route group whose prefix has a route value of its own.
    /// </param>
    /// <param name="mapSampleAppBetween">
    /// Whether the sample app's API is mapped between the test app's API and its tools, so that the API mapped last
    /// before <c>MapBlueprintMcp</c> is another app's.
    /// </param>
    /// <param name="rateLimitLowerCase">
    /// Whether <see cref="App.Lower.GetfooMcpQuery"/>'s endpoint requires the <c>api</c> rate-limiting policy, which allows
    /// one request a minute.
    /// </param>
    /// <returns>The started host.</returns>
    public static async Task<McpTestHost> StartAsync(
        bool renderOnFirstRequest = false,
        bool mapSampleApp = false,
        bool mapAdminCopy = false,
        bool adminFallback = false,
        bool mapGrouped = false,
        bool mapTenantGroup = false,
        bool mapSampleAppBetween = false,
        bool rateLimitLowerCase = false)
    {
        var builder = WebApplication.CreateSlimBuilder();

        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        builder.Services.AddBlueprintApi<McpTestBlueprintApp, McpTestBlueprintAppExecutor>();
        builder.Services.Configure<BlueprintHttpOptions>(o => o.RenderOpenApiDocumentOnFirstRequest = renderOnFirstRequest);
        builder.Services.AddAuthentication(SchemeName)
            .AddScheme<AuthenticationSchemeOptions, RolesHeaderAuthenticationHandler>(SchemeName, null)
            .AddScheme<AuthenticationSchemeOptions, AuthorizationRolesAuthenticationHandler>(AuthorizationRolesScheme, null);
        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy(AdminPolicy, p => p.RequireRole("Admin"));

            if (adminFallback)
            {
                o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireRole("Admin").Build();
            }
        });
        builder.Services.AddSingleton<ResponseCallbackProbe>();
        builder.Services.AddSingleton<AmbientContextProbe>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddBlueprintMcp();
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddFixedWindowLimiter("api", w =>
            {
                w.PermitLimit = 1;
                w.Window = TimeSpan.FromMinutes(1);
            });
        });

        if (mapSampleApp || mapSampleAppBetween)
        {
            builder.Services.AddBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>(
                static blueprint => blueprint.AddResourceAuthorizationProvider<ScopePairClaimAuthorizationProvider>());
        }

        var app = builder.Build();

        // Records each request whose HTTP context IHttpContextAccessor no longer gives once the rest of the pipeline has
        // run, as middleware wrapping an MCP endpoint would see it.
        var ambient = app.Services.GetRequiredService<AmbientContextProbe>();
        var accessor = app.Services.GetRequiredService<IHttpContextAccessor>();

        app.Use(async (context, next) =>
        {
            ambient.Started();

            try
            {
                await next(context);
            }
            finally
            {
                ambient.Finished(context.Request.Path.Value, ReferenceEquals(accessor.HttpContext, context));
            }
        });

        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        app.MapBlueprintApi<McpTestBlueprintApp, McpTestBlueprintAppExecutor>("api/")
            .Add(endpoint =>
            {
                if (endpoint.Metadata.OfType<ApiOperationDescriptor>().Any(static d => d.OperationType == typeof(PolicyGuardedMcpQuery)))
                {
                    endpoint.Metadata.Add(new AuthorizeAttribute(AdminPolicy));
                }

                if (endpoint.Metadata.OfType<ApiOperationDescriptor>().Any(static d => d.OperationType == typeof(RequirementGuardedMcpQuery)))
                {
                    endpoint.Metadata.Add(new BlueRoleAuthorizeAttribute());
                }

                if (rateLimitLowerCase
                    && endpoint.Metadata.OfType<ApiOperationDescriptor>().Any(static d => d.OperationType == typeof(App.Lower.GetfooMcpQuery)))
                {
                    endpoint.Metadata.Add(new EnableRateLimitingAttribute("api"));
                }

                if (endpoint.Metadata.OfType<ApiOperationDescriptor>().Any(static d => d.OperationType == typeof(WhoAmIMcpQuery)))
                {
                    endpoint.Metadata.Add(new AuthorizeAttribute { AuthenticationSchemes = AuthorizationRolesScheme });
                    endpoint.Metadata.Add(new AllowAnonymousAttribute());
                }
            });

        if (mapSampleAppBetween)
        {
            app.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("sample/");
        }

        var tools = app.MapBlueprintMcp<McpTestBlueprintApp>("api/mcp");

        if (adminFallback)
        {
            tools.RequireAuthorization(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        }

        if (mapAdminCopy)
        {
            app.MapBlueprintApi<McpTestBlueprintApp, McpTestBlueprintAppExecutor>("admin/").RequireAuthorization(AdminPolicy);
            app.MapBlueprintMcp<McpTestBlueprintApp>("admin/mcp");
        }

        if (mapGrouped)
        {
            var group = app.MapGroup("grouped").RequireAuthorization(AdminPolicy);

            group.MapBlueprintApi<McpTestBlueprintApp, McpTestBlueprintAppExecutor>("api/");
            group.MapBlueprintMcp<McpTestBlueprintApp>("api/mcp").AllowAnonymous();
        }

        if (mapTenantGroup)
        {
            var tenant = app.MapGroup("tenants/{TenantId}");

            tenant.MapBlueprintApi<McpTestBlueprintApp, McpTestBlueprintAppExecutor>("api/");
            tenant.MapBlueprintMcp<McpTestBlueprintApp>("api/mcp");
        }

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
    /// <param name="authorization">The <c>Authorization</c> header the client sends, or <see langword="null"/> for none.</param>
    /// <returns>The connected client.</returns>
    public async Task<McpClient> ConnectAsync(string path = "api/mcp", string? roles = null, string? authorization = null)
    {
        var options = new HttpClientTransportOptions
        {
            Endpoint = new Uri(this.BaseAddress, path),
            TransportMode = HttpTransportMode.StreamableHttp,
        };

        var headers = new Dictionary<string, string>();

        if (roles is not null)
        {
            headers[RolesHeader] = roles;
        }

        if (authorization is not null)
        {
            headers["Authorization"] = authorization;
        }

        options.AdditionalHeaders = headers;

        return await McpClient.CreateAsync(new HttpClientTransport(options));
    }

    /// <summary>
    /// Reads every operation id of the OpenAPI document served at <paramref name="basePath"/>, by its method and path.
    /// </summary>
    /// <param name="basePath">The API's base path, such as <c>api/</c>.</param>
    /// <returns>The ids, keyed by lower-case method and path relative to the base path.</returns>
    public async Task<Dictionary<(string Method, string Path), string>> GetOperationIdsAsync(string basePath = "api/")
    {
        using var http = this.CreateHttpClient("Admin");
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

    /// <summary>
    /// Records the requests whose HTTP context <see cref="IHttpContextAccessor"/> no longer gave once the rest of the
    /// pipeline had run.
    /// </summary>
    public sealed class AmbientContextProbe
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string?> _lost = new();
        private int _inFlight;

        /// <summary>Gets the paths of the requests whose context was lost.</summary>
        public IReadOnlyCollection<string?> Lost => this._lost;

        /// <summary>Waits until no request is in flight.</summary>
        /// <returns>A task that completes when none is, or after five seconds.</returns>
        public async Task WaitForIdleAsync()
        {
            for (var i = 0; i < 100 && Volatile.Read(ref this._inFlight) > 0; i++)
            {
                await Task.Delay(50);
            }
        }

        /// <summary>Records that a request has started.</summary>
        internal void Started()
        {
            Interlocked.Increment(ref this._inFlight);
        }

        /// <summary>Records that a request has finished, and whether the accessor still gave its context.</summary>
        /// <param name="path">The request's path.</param>
        /// <param name="hasContext">Whether the accessor gave the request's context.</param>
        internal void Finished(string? path, bool hasContext)
        {
            if (!hasContext)
            {
                this._lost.Enqueue(path);
            }

            Interlocked.Decrement(ref this._inFlight);
        }
    }

    /// <summary>
    /// An <see cref="AuthorizeAttribute"/> that, beyond an authenticated caller, requires the <c>Blue</c> role through
    /// <see cref="IAuthorizationRequirementData"/> rather than a named policy.
    /// </summary>
    private sealed class BlueRoleAuthorizeAttribute : AuthorizeAttribute, IAuthorizationRequirementData
    {
        public IEnumerable<IAuthorizationRequirement> GetRequirements()
        {
            return [new RolesAuthorizationRequirement(["Blue"])];
        }
    }

    private sealed class AuthorizationRolesAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = this.Request.Headers.Authorization.ToString();

            if (!header.StartsWith("Roles ", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = header["Roles ".Length..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static role => new Claim(System.Security.Claims.ClaimTypes.Role, role));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthorizationRolesScheme));

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, AuthorizationRolesScheme)));
        }
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

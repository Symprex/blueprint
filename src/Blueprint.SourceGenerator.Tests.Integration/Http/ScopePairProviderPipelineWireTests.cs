using System.Net.Http.Headers;
using System.Security.Claims;
using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves <see cref="ScopePairClaimAuthorizationProvider"/>'s Signature 365 parity settings through the generated pipeline
/// of a permissioned operation, served by Kestrel as a consumer serves it, rather than by calling the provider directly.
/// </summary>
/// <remarks>
/// The provider is configured as Signature 365 configures its scope check: an exact ignored-scope set with no built-in
/// additions, a verb hierarchy deeper than one level, full access for a token with no API scopes, and abstaining for an
/// unauthenticated caller.
/// </remarks>
public sealed class ScopePairProviderPipelineWireTests
{
    [Theory]
    [InlineData("ScopePairWidget::Read", 200)]
    [InlineData("ScopePairWidget::Manage", 200)]
    [InlineData("ScopePairWidget::Delete", 403)]
    [InlineData("OtherNoun::Manage", 403)]
    public async Task Verb_inclusions_are_transitive_in_the_pipeline(string scope, int expectedStatus)
    {
        // Manage includes Update, which includes Read, so Manage satisfies Read two levels down
        await using var host = await ScopePairHost.StartAsync();

        (await host.GetStatusAsync(authenticated: true, scope)).Should().Be(expectedStatus);
    }

    [Fact]
    public async Task A_scope_left_out_of_the_exact_ignored_set_counts_as_an_api_scope()
    {
        // The set given is exact, so offline_access is not ignored: it is a scope that matches no pair, so the token is
        // scoped and refused, as Signature 365 refuses it
        await using var host = await ScopePairHost.StartAsync();

        (await host.GetStatusAsync(authenticated: true, "offline_access")).Should().Be(403);
    }

    [Theory]
    [InlineData("openid")]
    [InlineData("openid", "profile")]
    [InlineData]
    public async Task A_token_with_no_api_scope_has_full_access(params string[] scopes)
    {
        await using var host = await ScopePairHost.StartAsync();

        (await host.GetStatusAsync(authenticated: true, scopes)).Should().Be(200);
    }

    [Fact]
    public async Task Allowing_unauthenticated_callers_does_not_open_a_permissioned_operation()
    {
        // The provider abstains for an unauthenticated caller, but the pipeline's authentication step refuses the caller
        // with a 401 before any provider is asked
        await using var host = await ScopePairHost.StartAsync();

        (await host.GetStatusAsync(authenticated: false, "ScopePairWidget::Read")).Should().Be(401);
    }

    private sealed class ScopePairHost : IAsyncDisposable
    {
        private const string IdentityHeader = "X-Test-Identity";
        private const string ScopesHeader = "X-Test-Scopes";

        private readonly WebApplication _app;
        private readonly System.Net.Http.HttpClient _client;

        private ScopePairHost(WebApplication app, System.Net.Http.HttpClient client)
        {
            this._app = app;
            this._client = client;
        }

        public static async Task<ScopePairHost> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();

            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(blueprint =>
                blueprint.AddResourceAuthorizationProvider(
                    new ScopePairClaimAuthorizationProvider(
                        scopeClaimType: "scope",
                        separator: "::",
                        ignoredScopes: ["openid", "profile"],
                        verbInclusions: new Dictionary<string, string[]>
                        {
                            ["Manage"] = ["Update"],
                            ["Update"] = ["Read"],
                        },
                        allowWhenNoScopeClaims: true,
                        allowUnauthenticated: true)));

            var app = builder.Build();

            app.Use(static (httpContext, next) =>
            {
                var authenticated = httpContext.Request.Headers[IdentityHeader] == "authenticated";
                var claims = new List<Claim> { new(System.Security.Claims.ClaimTypes.Name, "user-1") };

                foreach (var scope in httpContext.Request.Headers[ScopesHeader].ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    claims.Add(new Claim("scope", scope));
                }

                // An identity with no authentication type is not authenticated, whatever claims it carries
                httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "Test" : null));

                return next(httpContext);
            });

            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/api/") };

            return new ScopePairHost(app, client);
        }

        public async Task<int> GetStatusAsync(bool authenticated, params string[] scopes)
        {
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "scope-pair/widgets");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add(IdentityHeader, authenticated ? "authenticated" : "unauthenticated");
            request.Headers.Add(ScopesHeader, string.Join(' ', scopes));

            using var response = await this._client.SendAsync(request);

            return (int)response.StatusCode;
        }

        public async ValueTask DisposeAsync()
        {
            this._client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }
    }
}

/// <summary>Reads widgets, requiring <c>ScopePairWidget::Read</c>.</summary>
[Resource("ScopePairWidget", "Read")]
[RootLink("/scope-pair/widgets")]
public partial class GetScopePairWidgetsQuery : IQuery<string>
{
    public static string Handle(GetScopePairWidgetsQuery query)
    {
        return "widgets";
    }
}

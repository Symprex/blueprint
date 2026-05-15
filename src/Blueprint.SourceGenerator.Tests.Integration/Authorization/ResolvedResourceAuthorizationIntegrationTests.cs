using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Integration.Authorization;

/// <summary>
/// Proves that the generated pipeline expands resource key template tokens inline
/// and delegates the resolved scope pairs to <see cref="IResourceAuthorizationProvider"/>.
/// </summary>
public class ResolvedResourceAuthorizationIntegrationTests
{
    [Fact]
    public async Task When_resolved_provider_registered_then_receives_expanded_keys()
    {
        var provider = new CapturingResolvedProvider(authorized: true);

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IntegrationTestBlueprintApp>()
            .AddSingleton<IntegrationTestBlueprintAppExecutor>()
            .AddSingleton<IResourceAuthorizationProvider>(provider);

        var endpoints = new TestEndpointRouteBuilder(services.BuildServiceProvider());
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/tenants/{TenantId}/resources/{Id}");
        endpoint.Should().NotBeNull("an endpoint should be registered for E2EResourceKeyQuery");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, ctx =>
        {
            SetAuthenticatedUser(ctx, "acct-42");
            ctx.Request.RouteValues["TenantId"] = "T100";
            ctx.Request.RouteValues["Id"] = "R200";
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);

        provider.CapturedScopePairs.Should().NotBeNull("the resolved provider should have been invoked");
        provider.CapturedScopePairs.Should().HaveCount(1);

        var pair = provider.CapturedScopePairs![0];
        pair.Noun.Should().Be("resource");
        pair.Verb.Should().Be("read");
        pair.ResourceKeyTemplate.Should().Be("/tenants/{Auth:AccountId}/resources/{Id}");
        pair.ExpandedResourceKey.Should().Be("/tenants/acct-42/resources/R200",
            "Auth:AccountId should resolve from claims, Id from operation property");
    }

    [Fact]
    public async Task When_resolved_provider_denies_then_returns_403()
    {
        var provider = new CapturingResolvedProvider(authorized: false);

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IntegrationTestBlueprintApp>()
            .AddSingleton<IntegrationTestBlueprintAppExecutor>()
            .AddSingleton<IResourceAuthorizationProvider>(provider);

        var endpoints = new TestEndpointRouteBuilder(services.BuildServiceProvider());
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/tenants/{TenantId}/resources/{Id}");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, ctx =>
        {
            SetAuthenticatedUser(ctx, "acct-42");
            ctx.Request.RouteValues["TenantId"] = "T100";
            ctx.Request.RouteValues["Id"] = "R200";
        });

        response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task When_only_legacy_provider_registered_then_receives_scope_pairs()
    {
        var provider = new CapturingLegacyProvider(authorized: true);

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IntegrationTestBlueprintApp>()
            .AddSingleton<IntegrationTestBlueprintAppExecutor>()
            .AddSingleton<IResourceAuthorizationProvider>(provider);

        var endpoints = new TestEndpointRouteBuilder(services.BuildServiceProvider());
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/tenants/{TenantId}/resources/{Id}");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, ctx =>
        {
            SetAuthenticatedUser(ctx, "acct-42");
            ctx.Request.RouteValues["TenantId"] = "T100";
            ctx.Request.RouteValues["Id"] = "R200";
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);

        provider.CapturedScopePairs.Should().NotBeNull("legacy provider should be invoked as fallback");
        provider.CapturedScopePairs.Should().HaveCount(1);
        provider.CapturedScopePairs![0].Noun.Should().Be("resource");
        provider.CapturedScopePairs![0].Verb.Should().Be("read");
        provider.CapturedScopePairs![0].ResourceKeyTemplate.Should().Be("/tenants/{Auth:AccountId}/resources/{Id}");
    }

    // ─── Helpers ──────────────────────────────────────────────────────

    private static void SetAuthenticatedUser(DefaultHttpContext context, string accountId)
    {
        context.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "test-user"),
                    new System.Security.Claims.Claim("AccountId", accountId),
                ],
                "TestAuth"));
    }

    private static async Task<TestHttpResponse> InvokeEndpointAsync(
        RouteEndpoint endpoint,
        IServiceProvider serviceProvider,
        Action<DefaultHttpContext>? configureRequest = null)
    {
        await using var body = new System.IO.MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider,
            Response = { Body = body },
        };

        context.SetEndpoint(endpoint);
        var rawPath = endpoint.RoutePattern.RawText ?? string.Empty;
        context.Request.Path = "/" + rawPath.TrimStart('/');

        configureRequest?.Invoke(context);

        await endpoint.RequestDelegate!(context);
        body.Position = 0;

        return new TestHttpResponse(
            context.Response.StatusCode,
            context.Response.ContentType,
            System.Text.Encoding.UTF8.GetString(body.ToArray()));
    }

    private static RouteEndpoint? FindEndpoint(TestEndpointRouteBuilder builder, string routePattern)
    {
        return builder.DataSources
            .SelectMany(static ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .SingleOrDefault(ep => string.Equals(ep.RoutePattern.RawText, routePattern, StringComparison.Ordinal));
    }

    private sealed record TestHttpResponse(int StatusCode, string? ContentType, string Body);

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public ICollection<EndpointDataSource> DataSources { get; } = [];

        public IServiceProvider ServiceProvider { get; } = serviceProvider;

        public IApplicationBuilder CreateApplicationBuilder()
        {
            return new ApplicationBuilder(this.ServiceProvider);
        }
    }

    // ─── Test doubles ─────────────────────────────────────────────────

    private sealed class CapturingResolvedProvider(bool authorized) : IResourceAuthorizationProvider
    {
        public IReadOnlyList<Blueprint.Model.ResolvedScopePair>? CapturedScopePairs { get; private set; }

        public Task<bool> IsAuthorizedAsync(
            ApiOperationContext operationContext,
            System.Security.Claims.ClaimsPrincipal principal,
            IReadOnlyList<Blueprint.Model.ResolvedScopePair> resolvedScopePairs,
            CancellationToken cancellationToken)
        {
            this.CapturedScopePairs = resolvedScopePairs;
            return Task.FromResult(authorized);
        }
    }

    private sealed class CapturingLegacyProvider(bool authorized) : IResourceAuthorizationProvider
    {
        public IReadOnlyList<Blueprint.Model.ResolvedScopePair>? CapturedScopePairs { get; private set; }

        public Task<bool> IsAuthorizedAsync(
            ApiOperationContext operationContext,
            System.Security.Claims.ClaimsPrincipal principal,
            IReadOnlyList<Blueprint.Model.ResolvedScopePair> scopePairs,
            CancellationToken cancellationToken)
        {
            this.CapturedScopePairs = scopePairs;
            return Task.FromResult(authorized);
        }
    }
}

// ─── Test operations ──────────────────────────────────────────────────────────

/// <summary>
/// A test operation with resource key template tokens that exercise both property
/// and claim-based token expansion in the generated pipeline.
/// </summary>
[RootLink("/tenants/{TenantId}/resources/{Id}")]
[Resource("resource", "read", "/tenants/{Auth:AccountId}/resources/{Id}")]
public partial class E2EResourceKeyQuery : IQuery<string>
{
    public string TenantId { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    /// <summary>Inline handler for test simplicity.</summary>
    /// <param name="operation">The query instance carrying route-bound property values.</param>
    /// <returns>A string confirming the property values were bound.</returns>
    public static Task<string> HandleAsync(E2EResourceKeyQuery operation)
    {
        return Task.FromResult($"ok:{operation.TenantId}:{operation.Id}");
    }
}

using System.Security.Claims;
using System.Text;
using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Integration.Authorization;

/// <summary>
/// End-to-end integration tests proving that the generated pipeline correctly invokes
/// <see cref="IApiAuthoriser"/> implementations and populates
/// <see cref="ApiOperationContext.UserAuthorisationContext"/> via
/// <see cref="IUserAuthorisationContextFactory"/>.
/// </summary>
public class ApiAuthoriserIntegrationTests
{
    [Fact]
    public async Task When_authoriser_allows_then_returns_200()
    {
        var endpoints = CreateEndpointsWithAuthoriser(ExecutionAllowed.Yes);

        var endpoint = FindEndpoint(endpoints, "api/e2e/protected");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, SetAuthenticatedUser);

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task When_authoriser_denies_with_authorisation_failure_then_returns_403()
    {
        var endpoints = CreateEndpointsWithAuthoriser(
            ExecutionAllowed.No("Insufficient permissions", "not-permitted", ExecutionAllowedFailureType.Authorisation));

        var endpoint = FindEndpoint(endpoints, "api/e2e/protected");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, SetAuthenticatedUser);

        response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task When_authoriser_denies_with_authentication_failure_then_returns_401()
    {
        var endpoints = CreateEndpointsWithAuthoriser(
            ExecutionAllowed.No("Session expired", "session-expired", ExecutionAllowedFailureType.Authentication));

        var endpoint = FindEndpoint(endpoints, "api/e2e/protected");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, SetAuthenticatedUser);

        response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task When_no_authoriser_registered_then_authenticated_request_passes()
    {
        // No IApiAuthoriser registered — pipeline should pass through
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IntegrationTestBlueprintApp>()
            .AddSingleton<IntegrationTestBlueprintAppExecutor>();

        var endpoints = new TestEndpointRouteBuilder(services.BuildServiceProvider());
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/protected");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, SetAuthenticatedUser);

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task When_user_context_factory_registered_then_context_is_populated()
    {
        var factory = new TestUserContextFactory("user-42", "acct-7");
        var authoriser = new ContextInspectingAuthoriser();

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IntegrationTestBlueprintApp>()
            .AddSingleton<IntegrationTestBlueprintAppExecutor>()
            .AddSingleton<IUserAuthorisationContextFactory>(factory)
            .AddSingleton<IApiAuthoriser>(authoriser);

        var endpoints = new TestEndpointRouteBuilder(services.BuildServiceProvider());
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/protected");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, SetAuthenticatedUser);

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        authoriser.CapturedContext.Should().NotBeNull("the factory should have populated the user context");
        authoriser.CapturedContext!.Id.Should().Be("user-42");
        authoriser.CapturedContext!.AccountId.Should().Be("acct-7");
    }

    [Fact]
    public async Task When_anonymous_operation_then_authoriser_is_not_invoked()
    {
        var authoriser = new TrackingAuthoriser(ExecutionAllowed.No("should-never-reach", "blocked", ExecutionAllowedFailureType.Authorisation));

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IntegrationTestBlueprintApp>()
            .AddSingleton<IntegrationTestBlueprintAppExecutor>()
            .AddSingleton<IApiAuthoriser>(authoriser);

        var endpoints = new TestEndpointRouteBuilder(services.BuildServiceProvider());
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/echo");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.QueryString = new QueryString("?message=hello");
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        authoriser.WasInvoked.Should().BeFalse("anonymous operations should skip IApiAuthoriser checks");
    }

    [Fact]
    public async Task When_authoriser_does_not_apply_then_request_passes()
    {
        var authoriser = new SelectiveAuthoriser(
            appliesTo: false,
            result: ExecutionAllowed.No("blocked", "denied", ExecutionAllowedFailureType.Authorisation));

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IntegrationTestBlueprintApp>()
            .AddSingleton<IntegrationTestBlueprintAppExecutor>()
            .AddSingleton<IApiAuthoriser>(authoriser);

        var endpoints = new TestEndpointRouteBuilder(services.BuildServiceProvider());
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/protected");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, SetAuthenticatedUser);

        response.StatusCode.Should().Be(StatusCodes.Status200OK, "authorizer that doesn't apply should not block");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────────

    private static void SetAuthenticatedUser(DefaultHttpContext context)
    {
        context.User = new ClaimsPrincipal(new ClaimsIdentity("TestAuth"));
    }

    private static TestEndpointRouteBuilder CreateEndpointsWithAuthoriser(ExecutionAllowed result)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IntegrationTestBlueprintApp>()
            .AddSingleton<IntegrationTestBlueprintAppExecutor>()
            .AddSingleton<IApiAuthoriser>(new TestApiAuthoriser(result));

        var endpoints = new TestEndpointRouteBuilder(services.BuildServiceProvider());
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");
        return endpoints;
    }

    private static async Task<TestHttpResponse> InvokeEndpointAsync(
        RouteEndpoint endpoint,
        IServiceProvider serviceProvider,
        Action<DefaultHttpContext>? configureRequest = null)
    {
        await using var body = new MemoryStream();
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
            Encoding.UTF8.GetString(body.ToArray()));
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

    // ─── Test doubles ─────────────────────────────────────────────────────────────

    private sealed class TestApiAuthoriser(ExecutionAllowed result) : IApiAuthoriser
    {
        public bool AppliesTo(ApiOperationDescriptor descriptor)
        {
            return true;
        }

        public Task<ExecutionAllowed> CanExecuteOperationAsync(
            ApiOperationContext operationContext,
            ApiOperationDescriptor descriptor,
            object operation)
        {
            return Task.FromResult(result);
        }

        public Task<ExecutionAllowed> CanShowLinkAsync(
            ApiOperationContext operationContext,
            ApiOperationDescriptor descriptor,
            object resource)
        {
            return ExecutionAllowed.YesTask;
        }
    }

    private sealed class TrackingAuthoriser(ExecutionAllowed result) : IApiAuthoriser
    {
        public bool WasInvoked { get; private set; }

        public bool AppliesTo(ApiOperationDescriptor descriptor)
        {
            return true;
        }

        public Task<ExecutionAllowed> CanExecuteOperationAsync(
            ApiOperationContext operationContext,
            ApiOperationDescriptor descriptor,
            object operation)
        {
            this.WasInvoked = true;
            return Task.FromResult(result);
        }

        public Task<ExecutionAllowed> CanShowLinkAsync(
            ApiOperationContext operationContext,
            ApiOperationDescriptor descriptor,
            object resource)
        {
            return ExecutionAllowed.YesTask;
        }
    }

    private sealed class SelectiveAuthoriser(bool appliesTo, ExecutionAllowed result) : IApiAuthoriser
    {
        public bool AppliesTo(ApiOperationDescriptor descriptor)
        {
            return appliesTo;
        }

        public Task<ExecutionAllowed> CanExecuteOperationAsync(
            ApiOperationContext operationContext,
            ApiOperationDescriptor descriptor,
            object operation)
        {
            return Task.FromResult(result);
        }

        public Task<ExecutionAllowed> CanShowLinkAsync(
            ApiOperationContext operationContext,
            ApiOperationDescriptor descriptor,
            object resource)
        {
            return ExecutionAllowed.YesTask;
        }
    }

    private sealed class ContextInspectingAuthoriser : IApiAuthoriser
    {
        public IUserAuthorisationContext? CapturedContext { get; private set; }

        public bool AppliesTo(ApiOperationDescriptor descriptor)
        {
            return true;
        }

        public Task<ExecutionAllowed> CanExecuteOperationAsync(
            ApiOperationContext operationContext,
            ApiOperationDescriptor descriptor,
            object operation)
        {
            this.CapturedContext = operationContext.UserAuthorisationContext;
            return ExecutionAllowed.YesTask;
        }

        public Task<ExecutionAllowed> CanShowLinkAsync(
            ApiOperationContext operationContext,
            ApiOperationDescriptor descriptor,
            object resource)
        {
            return ExecutionAllowed.YesTask;
        }
    }

    private sealed class TestUserContextFactory(string userId, string accountId) : IUserAuthorisationContextFactory
    {
        public Task<IUserAuthorisationContext?> CreateContextAsync(ClaimsIdentity identity, CancellationToken cancellationToken)
        {
            return Task.FromResult<IUserAuthorisationContext?>(new TestUserContext(userId, accountId));
        }
    }

    private sealed class TestUserContext(string id, string accountId) : IUserAuthorisationContext
    {
        public bool IsActive => true;

        public bool IsAnonymous => false;

        public string Id => id;

        public string IdentityId => id;

        public string AccountId => accountId;

        public string Email => $"{id}@test.com";

        public string Name => $"Test User {id}";

        public void PopulateMetadata(Action<string, object> add)
        {
            add("test.user_id", this.Id);
        }
    }
}

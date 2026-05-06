using System.ComponentModel.DataAnnotations;
using System.Text;
using Blueprint.Model;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that the full Blueprint HTTP stack — endpoint mapping, generated pipeline execution,
/// and HTTP result translation — works end-to-end using the generated executor and real
/// source-generated pipelines.
/// </summary>
public class HttpEndToEndIntegrationTests
{
    [Fact]
    public async Task When_GET_anonymous_with_query_param_then_returns_200_with_query_value_in_body()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/echo");
        endpoint.Should().NotBeNull("the echo query route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.QueryString = new QueryString("?message=hello-e2e");
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Be("\"hello-e2e\"");
    }

    [Fact]
    public async Task When_GET_authenticated_operation_without_user_then_returns_401()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/protected");
        endpoint.Should().NotBeNull("the protected query route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task When_GET_resource_protected_operation_without_user_then_returns_401()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/resource-protected");
        endpoint.Should().NotBeNull("the resource-protected query route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task When_GET_from_resource_inherited_operation_without_user_then_returns_401()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/from-resource-inherited");
        endpoint.Should().NotBeNull("the FromResource-inherited query route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task When_POST_with_json_body_then_returns_200_with_bound_value_in_body()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/items");
        endpoint.Should().NotBeNull("the create command route should be registered");

        var bodyJson = """{"label":"e2e-test-label"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Be("\"created:e2e-test-label\"");
    }

    [Fact]
    public async Task When_POST_with_non_json_content_type_then_returns_415()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/items");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.ContentType = "text/plain; charset=utf-8";
            context.Request.ContentLength = 5;
            context.Request.Body = new MemoryStream("hello"u8.ToArray());
        });

        response.StatusCode.Should().Be(StatusCodes.Status415UnsupportedMediaType);
    }

    [Fact]
    public async Task When_POST_validation_fails_then_returns_422_with_validation_problem_details()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/validated");
        endpoint.Should().NotBeNull("the validated command route should be registered");

        // Send empty JSON object — Name is [Required] so validation should fail.
        var bodyJson = "{}"u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        response.ContentType.Should().StartWith("application/problem+json");
        response.Body.Should().Contain("Name");
    }

    [Fact]
    public async Task When_GET_resource_protected_with_authenticated_user_and_no_provider_then_returns_200()
    {
        // No IResourceAuthorizationProvider registered → permissive (authorization check skipped)
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/resource-protected");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity("TestAuth"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task When_GET_resource_protected_with_authenticated_user_and_provider_denies_then_returns_403()
    {
        var endpoints = CreateEndpointsWithAuthorization(authorized: false);
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/resource-protected");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity("TestAuth"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task When_GET_resource_protected_with_authenticated_user_and_provider_allows_then_returns_200()
    {
        var endpoints = CreateEndpointsWithAuthorization(authorized: true);
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/resource-protected");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity("TestAuth"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task When_handler_throws_ApiException_with_404_then_returns_404_with_problem_details()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/not-found-exception");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        response.ContentType.Should().Contain("application/problem+json");
        response.Body.Should().Contain("\"status\":404");
        response.Body.Should().Contain("Resource not found");
    }

    [Fact]
    public async Task When_handler_throws_unhandled_exception_then_returns_500_with_problem_details()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/unhandled-exception");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider);

        response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        response.ContentType.Should().Contain("application/problem+json");
        response.Body.Should().Contain("\"status\":500");
    }

    [Fact]
    public async Task When_GET_with_FromHeader_property_then_header_value_is_bound()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/header-binding");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Headers["X-Tenant-Id"] = "acme-corp";
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("tenant:acme-corp");
    }

    [Fact]
    public async Task When_GET_with_FromCookie_property_then_cookie_value_is_bound()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/cookie-binding");
        endpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Headers["Cookie"] = "session_token=abc123xyz";
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("session:abc123xyz");
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

    private static TestEndpointRouteBuilder CreateEndpoints()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IntegrationTestBlueprintApp>()
            .AddSingleton<IntegrationTestBlueprintAppExecutor>();

        return new TestEndpointRouteBuilder(services.BuildServiceProvider());
    }

    private static TestEndpointRouteBuilder CreateEndpointsWithAuthorization(bool authorized)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IntegrationTestBlueprintApp>()
            .AddSingleton<IntegrationTestBlueprintAppExecutor>()
            .AddSingleton<Blueprint.Authorization.IResourceAuthorizationProvider>(
                new TestResourceAuthorizationProvider(authorized));

        return new TestEndpointRouteBuilder(services.BuildServiceProvider());
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public ICollection<EndpointDataSource> DataSources { get; } = [];

        public IServiceProvider ServiceProvider { get; } = serviceProvider;

        public IApplicationBuilder CreateApplicationBuilder()
        {
            return new ApplicationBuilder(this.ServiceProvider);
        }
    }

    private sealed record TestHttpResponse(int StatusCode, string? ContentType, string Body);
}

// ─── E2E test operations ──────────────────────────────────────────────────────
// These are picked up by the IntegrationTestBlueprintApp scanner at compile time.

/// <summary>
/// Anonymous GET with a query-string parameter for end-to-end query-binding and routing validation.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/echo")]
public partial class E2EEchoQuery : IQuery<string>
{
    /// <summary>The message value to echo back in the response body.</summary>
    public string? Message { get; set; }

    public static Task<string> HandleAsync(E2EEchoQuery query)
    {
        return Task.FromResult(query.Message ?? "empty");
    }
}

/// <summary>
/// Authenticated GET — exercises the authentication pipeline rejection path (401) end-to-end.
/// </summary>
[Blueprint.Model.Attributes.MustBeAuthenticated]
[Blueprint.Model.Attributes.RootLink("/e2e/protected")]
public partial class E2EProtectedQuery : IQuery<string>
{
    public static Task<string> HandleAsync(E2EProtectedQuery query)
    {
        return Task.FromResult("protected-data");
    }
}

/// <summary>
/// Resource-permissioned GET — exercises the authorization-implied identity check path (401) end-to-end.
/// Operations with <c>[Resource]</c> implicitly require authentication even without <c>[MustBeAuthenticated]</c>.
/// </summary>
[Blueprint.Model.Attributes.Resource("account", "read")]
[Blueprint.Model.Attributes.RootLink("/e2e/resource-protected")]
public partial class E2EResourceProtectedQuery : IQuery<string>
{
    public static Task<string> HandleAsync(E2EResourceProtectedQuery query)
    {
        return Task.FromResult("resource-protected-data");
    }
}

/// <summary>
/// [FromResource]-inherited permission GET — proves the pipeline generates a 401 identity check for
/// operations that inherit scope pairs from another operation via <c>[FromResource(typeof(...))]</c>.
/// </summary>
[Blueprint.Model.Attributes.FromResource(typeof(E2EResourceProtectedQuery))]
[Blueprint.Model.Attributes.RootLink("/e2e/from-resource-inherited")]
public partial class E2EFromResourceInheritedQuery : IQuery<string>
{
    public static Task<string> HandleAsync(E2EFromResourceInheritedQuery query)
    {
        return Task.FromResult("from-resource-inherited-data");
    }
}

/// <summary>
/// POST command with a body-bound property for end-to-end body-binding and routing validation.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/items")]
public partial class E2ECreateCommand : ICommand<string>
{
    /// <summary>Body-bound label value reflected in the handler response.</summary>
    public string? Label { get; set; }

    public static Task<string> HandleAsync(E2ECreateCommand command)
    {
        return Task.FromResult($"created:{command.Label}");
    }
}

/// <summary>
/// POST command with a <see cref="RequiredAttribute"/> field for end-to-end validation-failure (422) testing.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/validated")]
public partial class E2EValidatedCommand : ICommand<string>
{
    /// <summary>Required name; omitting it triggers a 422 validation failure response.</summary>
    [Required]
    public string? Name { get; set; }

    public static Task<string> HandleAsync(E2EValidatedCommand command)
    {
        return Task.FromResult($"valid:{command.Name}");
    }
}

/// <summary>
/// Test implementation of <see cref="Blueprint.Authorization.IResourceAuthorizationProvider"/>
/// that returns a fixed authorization result for integration testing.
/// </summary>
internal sealed class TestResourceAuthorizationProvider(bool authorized) : Blueprint.Authorization.IResourceAuthorizationProvider
{
    public Task<bool> IsAuthorizedAsync(
        System.Security.Claims.ClaimsPrincipal principal,
        IReadOnlyList<Blueprint.Model.ScopePair> requiredScopePairs,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(authorized);
    }
}

/// <summary>
/// Query that throws an <see cref="ApiException"/> with HTTP 404 to verify non-500 exception translation.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/not-found-exception")]
public partial class E2ENotFoundExceptionQuery : IQuery<string>
{
    /// <summary>Throws ApiException(404) to test HTTP status translation.</summary>
    /// <param name="query">The query operation instance (unused).</param>
    /// <returns>Never returns — always throws.</returns>
    public static Task<string> HandleAsync(E2ENotFoundExceptionQuery query)
    {
        throw new ApiException("Resource not found", 404);
    }
}

/// <summary>
/// Query that throws a plain <see cref="InvalidOperationException"/> to verify 500 fallback.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/unhandled-exception")]
public partial class E2EUnhandledExceptionQuery : IQuery<string>
{
    /// <summary>Throws InvalidOperationException to test 500 fallback path.</summary>
    /// <param name="query">The query operation instance (unused).</param>
    /// <returns>Never returns — always throws.</returns>
    public static Task<string> HandleAsync(E2EUnhandledExceptionQuery query)
    {
        throw new InvalidOperationException("Something went wrong");
    }
}

/// <summary>
/// Query with a <see cref="Blueprint.Model.Attributes.FromHeaderAttribute"/> property to verify HTTP header binding.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/header-binding")]
public partial class E2EHeaderBindingQuery : IQuery<string>
{
    /// <summary>Bound from X-Tenant-Id request header.</summary>
    [Blueprint.Model.Attributes.FromHeader("X-Tenant-Id")]
    public string? TenantId { get; set; }

    /// <summary>Echoes the header-bound TenantId value.</summary>
    /// <param name="query">The query with the header-bound property.</param>
    /// <returns>The TenantId value read from the header.</returns>
    public static Task<string> HandleAsync(E2EHeaderBindingQuery query)
    {
        return Task.FromResult($"tenant:{query.TenantId}");
    }
}

/// <summary>
/// Query with a <see cref="Blueprint.Model.Attributes.FromCookieAttribute"/> property to verify HTTP cookie binding.
/// </summary>
[Blueprint.Model.Attributes.RootLink("/e2e/cookie-binding")]
public partial class E2ECookieBindingQuery : IQuery<string>
{
    /// <summary>Bound from session_token cookie.</summary>
    [Blueprint.Model.Attributes.FromCookie("session_token")]
    public string? SessionToken { get; set; }

    /// <summary>Echoes the cookie-bound session token value.</summary>
    /// <param name="query">The query with the cookie-bound property.</param>
    /// <returns>The SessionToken value read from the cookie.</returns>
    public static Task<string> HandleAsync(E2ECookieBindingQuery query)
    {
        return Task.FromResult($"session:{query.SessionToken}");
    }
}

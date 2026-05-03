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

using System.Security.Claims;
using System.Text;
using Blueprint.SourceGenerator.Sample;
using Blueprint.SourceGenerator.Sample.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves the curated sample app is runnable through the generated HTTP host with deterministic seeded behavior.
/// </summary>
public class ReferenceAppHttpEndToEndIntegrationTests
{
    [Fact]
    public async Task When_sample_templates_endpoint_is_invoked_without_authenticated_user_then_returns_401()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates", "GET");
        endpoint.Should().NotBeNull("the sample templates route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Get;
        });

        response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task When_sample_template_detail_endpoint_is_invoked_with_authenticated_user_then_returns_seeded_template()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}", "GET");
        endpoint.Should().NotBeNull("the sample template detail route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/templates/template-002";
            context.Request.RouteValues = new RouteValueDictionary
            {
                ["Id"] = "template-002",
            };
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().Be("application/json; charset=utf-8");
        response.Body.Should().Contain("template-002");
        response.Body.Should().Contain("Formal Greeting");
    }

    [Fact]
    public async Task When_get_template_with_unknown_id_then_returns_200_with_placeholder()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}", "GET");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/templates/unknown-999";
            context.Request.RouteValues = new RouteValueDictionary
            {
                ["Id"] = "unknown-999",
            };
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.Should().Contain("unknown-999");
        response.Body.Should().Contain("Template unknown-999");
    }

    [Fact]
    public async Task When_create_template_without_auth_then_returns_401()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates", "POST");
        endpoint.Should().NotBeNull("the create template route should be registered");

        var bodyJson = """{"name":"Test Template","htmlSource":"<p>Hello</p>","plainTextSource":"Hello"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
        });

        response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task When_create_template_with_valid_body_then_returns_200_with_template_resource()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates", "POST");
        endpoint.Should().NotBeNull("the create template route should be registered");

        var bodyJson = """{"name":"My Template","htmlSource":"<p>{{Name}}</p>","plainTextSource":"Hello {{Name}}"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().Be("application/json; charset=utf-8");
        response.Body.Should().Contain("template-");
        response.Body.Should().Contain("My Template");
    }

    [Fact]
    public async Task When_create_template_with_missing_required_name_then_returns_422_with_validation_errors()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates", "POST");
        endpoint.Should().NotBeNull("the create template route should be registered");

        var bodyJson = """{"htmlSource":"<p>No name</p>"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        response.ContentType.Should().StartWith("application/problem+json");
        response.Body.Should().Contain("Name");
    }

    [Fact]
    public async Task When_create_template_with_name_exceeding_max_length_then_returns_422_with_validation_errors()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates", "POST");
        endpoint.Should().NotBeNull("the create template route should be registered");

        var tooLongName = new string('x', 201);
        var bodyJson = System.Text.Encoding.UTF8.GetBytes($$$"""{"name":"{{{tooLongName}}}","htmlSource":"<p>test</p>"}""");
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        response.ContentType.Should().StartWith("application/problem+json");
        response.Body.Should().Contain("Name");
        response.Body.Should().Contain("200");
    }

    [Fact]
    public async Task When_update_template_without_auth_then_returns_401()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}", "PUT");
        endpoint.Should().NotBeNull("the update template route should be registered");

        var bodyJson = """{"name":"Updated","htmlSource":"<p>Updated</p>","plainTextSource":"Updated"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Put;
            context.Request.Path = "/api/templates/template-001";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-001" };
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
        });

        response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task When_update_template_with_valid_body_then_returns_200_with_updated_resource()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}", "PUT");

        var bodyJson = """{"name":"Renamed Template","htmlSource":"<p>New content</p>","plainTextSource":"New content"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Put;
            context.Request.Path = "/api/templates/template-001";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-001" };
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().Be("application/json; charset=utf-8");
        response.Body.Should().Contain("Renamed Template");
        response.Body.Should().Contain("New content");
    }

    [Fact]
    public async Task When_update_template_with_missing_name_then_returns_422()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}", "PUT");

        var bodyJson = """{"htmlSource":"<p>No name</p>"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Put;
            context.Request.Path = "/api/templates/template-001";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-001" };
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        response.ContentType.Should().StartWith("application/problem+json");
        response.Body.Should().Contain("Name");
    }

    [Fact]
    public async Task When_archive_template_without_auth_then_returns_401()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}", "DELETE");
        endpoint.Should().NotBeNull("the DELETE template route should be registered");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Delete;
            context.Request.Path = "/api/templates/template-001";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-001" };
        });

        response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task When_archive_template_with_auth_then_returns_200()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/templates/{Id}", "DELETE");

        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = HttpMethods.Delete;
            context.Request.Path = "/api/templates/template-001";
            context.Request.RouteValues = new RouteValueDictionary { ["Id"] = "template-001" };
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                ],
                authenticationType: "test"));
        });

        // Void commands produce 200 OK with empty body (NoResultOperationResult semantics).
        response.StatusCode.Should().Be(StatusCodes.Status200OK);
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
        context.Request.Path = "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');

        configureRequest?.Invoke(context);

        await endpoint.RequestDelegate!(context);
        body.Position = 0;

        return new TestHttpResponse(
            context.Response.StatusCode,
            context.Response.ContentType,
            Encoding.UTF8.GetString(body.ToArray()));
    }

    private static RouteEndpoint? FindEndpoint(TestEndpointRouteBuilder builder, string routePattern, string httpMethod)
    {
        return builder.DataSources
            .SelectMany(static dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .SingleOrDefault(endpoint =>
                string.Equals(endpoint.RoutePattern.RawText, routePattern, StringComparison.Ordinal)
                && endpoint.Metadata.GetMetadata<HttpMethodMetadata>() is { } meta
                && meta.HttpMethods.Contains(httpMethod, StringComparer.OrdinalIgnoreCase));
    }

    private static TestEndpointRouteBuilder CreateEndpoints()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TemplateStore>()
            .AddSingleton<SampleBlueprintApp>()
            .AddSingleton<SampleBlueprintAppExecutor>();

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

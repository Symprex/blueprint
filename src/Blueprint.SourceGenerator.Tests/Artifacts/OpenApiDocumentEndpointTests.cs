using System.Text;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Runtime.Http;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Blueprint.SourceGenerator.Tests.Artifacts;

public class OpenApiDocumentEndpointTests
{
    [Fact]
    public void When_blueprint_api_is_mapped_then_openapi_document_endpoint_is_registered_at_expected_path()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<OpenApiTestBlueprintApp, BlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/.blueprint/openapi.json");

        endpoint.Should().NotBeNull();
    }

    [Fact]
    public void When_blueprint_api_is_mapped_with_trailing_slash_then_openapi_endpoint_path_is_normalised()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<OpenApiTestBlueprintApp, BlueprintAppExecutor>("api");

        var endpoint = FindEndpoint(endpoints, "api/.blueprint/openapi.json");

        endpoint.Should().NotBeNull();
    }

    [Fact]
    public async Task When_openapi_endpoint_is_invoked_then_response_is_200_ok_with_json_content_type()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<OpenApiTestBlueprintApp, BlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/.blueprint/openapi.json")!;
        var response = await InvokeEndpointAsync(endpoint);

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().Be("application/json; charset=utf-8");
    }

    [Fact]
    public async Task When_openapi_endpoint_is_invoked_then_body_is_valid_openapi_3x_json()
    {
        var endpoints = CreateEndpoints();
        endpoints.MapBlueprintApi<OpenApiTestBlueprintApp, BlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/.blueprint/openapi.json")!;
        var response = await InvokeEndpointAsync(endpoint);

        response.Body.Should().NotBeNullOrEmpty();

        var doc = JsonDocument.Parse(response.Body);
        doc.RootElement.GetProperty("openapi").GetString().Should().StartWith("3.");
        doc.RootElement.TryGetProperty("info", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("paths", out _).Should().BeTrue();
    }

    [Fact]
    public void When_blueprint_api_is_mapped_in_production_then_openapi_endpoint_is_still_registered()
    {
        // OpenAPI is a required part of the HTTP host, not a development-only surface.
        var endpoints = CreateEndpoints(Environments.Production);
        endpoints.MapBlueprintApi<OpenApiTestBlueprintApp, BlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/.blueprint/openapi.json");

        endpoint.Should().NotBeNull();
    }

    private static async Task<TestEndpointInvocationResult> InvokeEndpointAsync(RouteEndpoint endpoint)
    {
        await using var body = new MemoryStream();
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = body,
            },
        };

        await endpoint.RequestDelegate!(context);
        body.Position = 0;

        return new TestEndpointInvocationResult(
            context.Response.StatusCode,
            context.Response.ContentType,
            Encoding.UTF8.GetString(body.ToArray()));
    }

    private static RouteEndpoint? FindEndpoint(TestEndpointRouteBuilder endpoints, string routePattern)
    {
        return endpoints.DataSources
            .SelectMany(static dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .SingleOrDefault(endpoint => string.Equals(endpoint.RoutePattern.RawText, routePattern, StringComparison.Ordinal));
    }

    private static TestEndpointRouteBuilder CreateEndpoints(string environmentName = "Development")
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment(environmentName))
            .AddSingleton<IOptions<BlueprintHttpOptions>>(Options.Create(new BlueprintHttpOptions()))
            .BuildServiceProvider();

        return new TestEndpointRouteBuilder(services);
    }

    private sealed class OpenApiTestBlueprintApp : BlueprintApp
    {
        public OpenApiTestBlueprintApp()
        {
            var descriptor = new ApiOperationDescriptor(typeof(TestOperation), "TestOperation", "OpenApiDocumentEndpointTests");
            descriptor.AddUrl(new ApiOperationUrl("/test"));
            this.Register(descriptor);
        }
    }

    private sealed class TestOperation;

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public ICollection<EndpointDataSource> DataSources { get; } = [];

        public IServiceProvider ServiceProvider { get; } = serviceProvider;

        public IApplicationBuilder CreateApplicationBuilder()
        {
            return new ApplicationBuilder(this.ServiceProvider);
        }
    }

    private sealed class TestWebHostEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Blueprint.SourceGenerator.Tests";

        public string WebRootPath { get; set; } = string.Empty;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed record TestEndpointInvocationResult(
        int StatusCode,
        string? ContentType,
        string Body);
}

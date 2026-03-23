using System.Text;
using Blueprint.Model;
using Blueprint.Runtime.Artifacts;
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

public class ManifestInspectionEndpointTests
{
    private const string ResourceName = "Blueprint.SourceGenerator.Tests.Artifacts.ManifestResources.operations.manifest.json";
    private const string SnapshotsFolder = "ManifestSnapshots/";

    [Fact]
    public async Task When_mapped_in_development_then_manifest_json_endpoint_returns_embedded_manifest_and_production_stays_dark()
    {
        var developmentEndpoints = CreateEndpoints(Environments.Development);
        developmentEndpoints.MapBlueprintApi<ManifestInspectionBlueprintApp, BlueprintAppExecutor>("api/");

        var jsonEndpoint = FindEndpoint(developmentEndpoints, "api/.blueprint/manifests/operations.json");

        jsonEndpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(jsonEndpoint!);
        var expectedJson = ManifestResourceLoader.LoadRequiredString(typeof(ManifestInspectionEndpointTests).Assembly, ResourceName);

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().Be("application/json; charset=utf-8");
        response.Body.Should().Be(expectedJson);

        var productionEndpoints = CreateEndpoints(Environments.Production);
        productionEndpoints.MapBlueprintApi<ManifestInspectionBlueprintApp, BlueprintAppExecutor>("api/");

        FindEndpoint(productionEndpoints, "api/.blueprint/manifests/operations.json").Should().BeNull();
    }

    [Fact]
    public async Task When_mapped_in_development_then_manifest_diagnostics_page_returns_html_shell_and_production_stays_dark()
    {
        var developmentEndpoints = CreateEndpoints(Environments.Development);
        developmentEndpoints.MapBlueprintApi<ManifestInspectionBlueprintApp, BlueprintAppExecutor>("api/");

        var htmlEndpoint = FindEndpoint(developmentEndpoints, "api/.blueprint/manifests/operations.html");

        htmlEndpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(htmlEndpoint!);

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().Be("text/html; charset=utf-8");

        await Verify(response.Body)
            .UseDirectory(SnapshotsFolder);

        var productionEndpoints = CreateEndpoints(Environments.Production);
        productionEndpoints.MapBlueprintApi<ManifestInspectionBlueprintApp, BlueprintAppExecutor>("api/");

        FindEndpoint(productionEndpoints, "api/.blueprint/manifests/operations.html").Should().BeNull();
    }

    [Fact]
    public async Task When_mapped_in_development_then_manifest_diagnostics_page_avoids_echoing_raw_fetch_errors()
    {
        var endpoints = CreateEndpoints(Environments.Development);
        endpoints.MapBlueprintApi<ManifestInspectionBlueprintApp, BlueprintAppExecutor>("api/");

        var htmlEndpoint = FindEndpoint(endpoints, "api/.blueprint/manifests/operations.html");

        htmlEndpoint.Should().NotBeNull();

        var response = await InvokeEndpointAsync(htmlEndpoint!);

        response.Body.Should().Contain("Check browser console for details.");
        response.Body.Should().Contain("console.error(\"Manifest load error:\", error);");
        response.Body.Should().NotContain("Unable to load canonical manifest: ${error.message ?? error}");
    }

    [Fact]
    public void When_manifest_resource_name_is_invalid_then_mapping_fails_fast()
    {
        var endpoints = CreateEndpoints(Environments.Development, "missing.manifest.json");

        var act = () => endpoints.MapBlueprintApi<ManifestInspectionBlueprintApp, BlueprintAppExecutor>("api/");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*missing.manifest.json*");
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

    private static TestEndpointRouteBuilder CreateEndpoints(string environmentName, string resourceName = ResourceName)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment(environmentName))
            .AddSingleton<IOptions<BlueprintHttpOptions>>(Options.Create(new BlueprintHttpOptions
            {
                OperationsManifestResourceName = resourceName,
            }))
            .BuildServiceProvider();

        return new TestEndpointRouteBuilder(services);
    }

    private sealed class ManifestInspectionBlueprintApp : BlueprintApp
    {
        public ManifestInspectionBlueprintApp()
        {
            var descriptor = new ApiOperationDescriptor(typeof(TestOperation), "TestOperation", "ManifestInspectionEndpointTests");
            descriptor.AddUrl(new ApiOperationUrl("/test-operation"));
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

        public string ApplicationName { get; set; } = typeof(ManifestInspectionEndpointTests).Assembly.GetName().Name ?? "Blueprint.SourceGenerator.Tests";

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

using Blueprint.Model;
using Blueprint.Runtime.Http;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Blueprint.SourceGenerator.Tests.Artifacts;

public class ManifestInspectionEndpointStressTests
{
    private const string ResourceName = "Blueprint.SourceGenerator.Tests.Artifacts.ManifestResources.operations.manifest.json";
    private const string MarkdownResourceName = "Blueprint.SourceGenerator.Tests.Artifacts.ManifestResources.operations.manifest.md";
    private const string SchemaResourceName = "Blueprint.SourceGenerator.Tests.Artifacts.ManifestResources.GeneratedManifestEnvelope.schema.json";

    [Fact]
    public async Task When_mapping_blueprint_api_repeatedly_then_stress_harness_reports_stable_endpoint_counts_and_hosted_manifest_routes()
    {
        var summary = await RepeatedEndpointMappingStressHarness.ExecuteAsync(
            iterationCount: 40,
            createEndpoints: static () => CreateEndpoints(Environments.Development),
            mapEndpoints: static endpoints => endpoints.MapBlueprintApi<StressManifestInspectionBlueprintApp, BlueprintAppExecutor>("api/"),
            requiredRoutePattern: "api/.blueprint/manifests/operations.json");

        summary.RequestedIterationCount.Should().Be(40);
        summary.CompletedIterationCount.Should().Be(40);
        summary.FaultedIterationCount.Should().Be(0);
        summary.Failures.Should().BeEmpty();
        summary.EndpointCounts.Should().OnlyContain(static count => count == 5);
        summary.RequiredRoutePatternHitCount.Should().Be(40);
    }

    private static StressTestEndpointRouteBuilder CreateEndpoints(string environmentName)
    {
        var httpOptions = new BlueprintHttpOptions
        {
            OperationsManifestResourceName = ResourceName,
            OperationsManifestMarkdownResourceName = MarkdownResourceName,
            OperationsManifestSchemaResourceName = SchemaResourceName,
        };

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IWebHostEnvironment>(new StressTestWebHostEnvironment(environmentName))
            .AddSingleton<IOptions<BlueprintHttpOptions>>(Options.Create(httpOptions))
            .BuildServiceProvider();

        return new StressTestEndpointRouteBuilder(services);
    }

    private sealed class StressManifestInspectionBlueprintApp : BlueprintApp
    {
        public StressManifestInspectionBlueprintApp()
        {
            var descriptor = new ApiOperationDescriptor(typeof(StressTestOperation), "StressTestOperation", "ManifestInspectionEndpointStressTests");
            descriptor.AddUrl(new ApiOperationUrl("/stress-test-operation"));
            this.Register(descriptor);
        }
    }

    private sealed class StressTestOperation;

    private sealed class StressTestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder, IDisposable
    {
        public ICollection<EndpointDataSource> DataSources { get; } = [];

        public IServiceProvider ServiceProvider { get; } = serviceProvider;

        public IApplicationBuilder CreateApplicationBuilder()
        {
            return new ApplicationBuilder(this.ServiceProvider);
        }

        public void Dispose()
        {
            if (this.ServiceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private sealed class StressTestWebHostEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = typeof(ManifestInspectionEndpointStressTests).Assembly.GetName().Name ?? "Blueprint.SourceGenerator.Tests";

        public string WebRootPath { get; set; } = string.Empty;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

internal static class RepeatedEndpointMappingStressHarness
{
    public static Task<RepeatedEndpointMappingStressSummary> ExecuteAsync<TEndpointRouteBuilder>(
        int iterationCount,
        Func<TEndpointRouteBuilder> createEndpoints,
        Action<TEndpointRouteBuilder> mapEndpoints,
        string requiredRoutePattern)
        where TEndpointRouteBuilder : IEndpointRouteBuilder, IDisposable
    {
        var endpointCounts = new List<int>(iterationCount);
        var failures = new List<RepeatedEndpointMappingStressFailure>();
        var completedIterationCount = 0;
        var faultedIterationCount = 0;
        var requiredRoutePatternHitCount = 0;

        for (var iteration = 0; iteration < iterationCount; iteration++)
        {
            try
            {
                using var endpoints = createEndpoints();
                mapEndpoints(endpoints);

                endpointCounts.Add(CountMappedEndpoints(endpoints));

                if (ContainsRoutePattern(endpoints, requiredRoutePattern))
                {
                    requiredRoutePatternHitCount++;
                }

                completedIterationCount++;
            }
            catch (Exception exception)
            {
                faultedIterationCount++;
                failures.Add(new RepeatedEndpointMappingStressFailure(iteration, exception.ToString()));
            }
        }

        return Task.FromResult(new RepeatedEndpointMappingStressSummary(
            iterationCount,
            completedIterationCount,
            faultedIterationCount,
            endpointCounts,
            failures,
            requiredRoutePatternHitCount));
    }

    private static int CountMappedEndpoints(IEndpointRouteBuilder endpoints)
    {
        return endpoints.DataSources.Sum(static dataSource => dataSource.Endpoints.Count);
    }

    private static bool ContainsRoutePattern(IEndpointRouteBuilder endpoints, string requiredRoutePattern)
    {
        return endpoints.DataSources
            .SelectMany(static dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Any(endpoint => string.Equals(endpoint.RoutePattern.RawText, requiredRoutePattern, StringComparison.Ordinal));
    }
}

internal sealed record RepeatedEndpointMappingStressSummary(
    int RequestedIterationCount,
    int CompletedIterationCount,
    int FaultedIterationCount,
    IReadOnlyList<int> EndpointCounts,
    IReadOnlyList<RepeatedEndpointMappingStressFailure> Failures,
    int RequiredRoutePatternHitCount);

internal sealed record RepeatedEndpointMappingStressFailure(
    int Iteration,
    string ExceptionText);

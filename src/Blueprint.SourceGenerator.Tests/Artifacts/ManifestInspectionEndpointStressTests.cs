using Blueprint.Model;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Benchmarks.RuntimeModel;
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
    public async Task When_mapping_blueprint_api_repeatedly_then_stress_harness_reports_shared_synthetic_fixture_endpoint_counts_and_all_hosted_manifest_routes()
    {
        var descriptors = BlueprintAppBenchmarkScenarioFactory.CreateDescriptors(operationCount: 10);
        var expectedEndpointCount = descriptors.Count + 8;
        var requiredRoutePatterns = new[]
        {
            // The root-metadata document legacy served at every HTTP app's base path (Signature365 8b40ecc961,
            // Blueprint.Http/BlueprintApiBuilderExtensions.cs:84-92, RootMetadataOperation.cs:13).
            "api/",
            "api/.blueprint/openapi.json",
            "api/.blueprint/manifests/operations.json",
            "api/.blueprint/manifests/operations.md",
            "api/.blueprint/manifests/operations.schema.json",
            "api/.blueprint/manifests/operations.html",
            "/.well-known/openapi",
            "/.well-known/api-catalog",
        };
        var originalDescriptors = SharedSyntheticManifestInspectionBlueprintApp.Descriptors;

        SharedSyntheticManifestInspectionBlueprintApp.Descriptors = descriptors;

        try
        {
            var summary = await RepeatedEndpointMappingStressHarness.ExecuteAsync(
                iterationCount: 40,
                createEndpoints: static () => CreateEndpoints(Environments.Development),
                mapEndpoints: static endpoints => endpoints.MapBlueprintApi<SharedSyntheticManifestInspectionBlueprintApp, BlueprintAppExecutor>("api/"),
                requiredRoutePatterns: requiredRoutePatterns);

            summary.RequestedIterationCount.Should().Be(40);
            summary.Failures.Should().BeEmpty();
            summary.CompletedIterationCount.Should().Be(40);
            summary.FaultedIterationCount.Should().Be(0);
            summary.EndpointCounts.Should().OnlyContain(count => count == expectedEndpointCount);
            summary.RequiredRoutePatternHitCounts.Should().BeEquivalentTo(
                requiredRoutePatterns.ToDictionary(
                    static routePattern => routePattern,
                    static _ => 40));
        }
        finally
        {
            SharedSyntheticManifestInspectionBlueprintApp.Descriptors = originalDescriptors;
        }
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
            .AddSingleton<BlueprintAppExecutor>(sp => new BlueprintAppExecutor(new SharedSyntheticManifestInspectionBlueprintApp()))
            .BuildServiceProvider();

        return new StressTestEndpointRouteBuilder(services);
    }

    private sealed class SharedSyntheticManifestInspectionBlueprintApp : BlueprintApp
    {
        public static IReadOnlyList<ApiOperationDescriptor> Descriptors { get; set; } = [];

        public SharedSyntheticManifestInspectionBlueprintApp()
        {
            foreach (var descriptor in Descriptors)
            {
                this.Register(descriptor);
            }
        }
    }

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
        IReadOnlyList<string> requiredRoutePatterns)
        where TEndpointRouteBuilder : IEndpointRouteBuilder, IDisposable
    {
        var endpointCounts = new List<int>(iterationCount);
        var failures = new List<RepeatedEndpointMappingStressFailure>();
        var completedIterationCount = 0;
        var faultedIterationCount = 0;
        var requiredRoutePatternHitCounts = requiredRoutePatterns.ToDictionary(
            static routePattern => routePattern,
            static _ => 0,
            StringComparer.Ordinal);

        for (var iteration = 0; iteration < iterationCount; iteration++)
        {
            try
            {
                using var endpoints = createEndpoints();
                mapEndpoints(endpoints);

                endpointCounts.Add(CountMappedEndpoints(endpoints));
                var mappedRoutePatterns = GetMappedRoutePatterns(endpoints);
                foreach (var requiredRoutePattern in requiredRoutePatterns)
                {
                    if (mappedRoutePatterns.Contains(requiredRoutePattern))
                    {
                        requiredRoutePatternHitCounts[requiredRoutePattern]++;
                    }
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
            requiredRoutePatternHitCounts));
    }

    private static int CountMappedEndpoints(IEndpointRouteBuilder endpoints)
    {
        return endpoints.DataSources.Sum(static dataSource => dataSource.Endpoints.Count);
    }

    private static HashSet<string> GetMappedRoutePatterns(IEndpointRouteBuilder endpoints)
    {
        return endpoints.DataSources
            .SelectMany(static dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(static endpoint => endpoint.RoutePattern.RawText)
            .Where(static rawText => rawText is not null)
            .Select(static rawText => rawText!)
            .ToHashSet(StringComparer.Ordinal);
    }
}

internal sealed record RepeatedEndpointMappingStressSummary(
    int RequestedIterationCount,
    int CompletedIterationCount,
    int FaultedIterationCount,
    IReadOnlyList<int> EndpointCounts,
    IReadOnlyList<RepeatedEndpointMappingStressFailure> Failures,
    IReadOnlyDictionary<string, int> RequiredRoutePatternHitCounts);

internal sealed record RepeatedEndpointMappingStressFailure(
    int Iteration,
    string ExceptionText);

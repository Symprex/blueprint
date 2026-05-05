extern alias benchmarks;

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;
using FluentAssertions;
using BlueprintAppBenchmarkScenarioFactory = benchmarks::Blueprint.SourceGenerator.Benchmarks.RuntimeModel.BlueprintAppBenchmarkScenarioFactory;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;

/// <summary>
/// Stress tests that prove OpenAPI document generation, app registration, and concurrent dispatch
/// remain stable at Tier 2 scale (100-250 operations) under sustained and concurrent usage.
/// </summary>
public class ScaleStressIntegrationTests
{
    [Fact]
    public void When_openapi_rendered_for_100_operations_then_document_is_valid_and_contains_expected_path_count()
    {
        var app = CreateHttpExposedApp(operationCount: 100);

        var json = RenderOpenApiDocument(app);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("openapi").GetString().Should().StartWith("3.");
        doc.RootElement.GetProperty("paths").EnumerateObject().Should().HaveCount(100);
    }

    [Fact]
    public void When_openapi_rendered_for_250_operations_then_document_is_valid_and_path_count_matches()
    {
        var app = CreateHttpExposedApp(operationCount: 250);

        var json = RenderOpenApiDocument(app);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("openapi").GetString().Should().StartWith("3.");
        doc.RootElement.GetProperty("paths").EnumerateObject().Should().HaveCount(250);
    }

    [Fact]
    public void When_openapi_rendered_repeatedly_for_100_operations_then_output_is_deterministic()
    {
        var app = CreateHttpExposedApp(operationCount: 100);

        var firstRender = RenderOpenApiDocument(app);

        for (var iteration = 0; iteration < 20; iteration++)
        {
            var subsequentRender = RenderOpenApiDocument(app);
            subsequentRender.Should().Be(firstRender, $"iteration {iteration} produced a different document");
        }
    }

    [Fact]
    public void When_registration_stress_performed_for_250_operations_over_50_iterations_then_all_succeed_deterministically()
    {
        var descriptors = BlueprintAppBenchmarkScenarioFactory.CreateDescriptors(operationCount: 250);
        var expectedCount = descriptors.Count;

        for (var iteration = 0; iteration < 50; iteration++)
        {
            var app = BlueprintAppBenchmarkScenarioFactory.CreateRegisteredApp(descriptors);
            app.Operations.Should().HaveCount(expectedCount, $"iteration {iteration} registered unexpected count");
        }
    }

    [Fact]
    public async Task When_256_concurrent_dispatches_against_100_operation_app_then_all_complete_without_faults()
    {
        const int dispatchCount = 256;
        const int operationCount = 100;

        var descriptors = BlueprintAppBenchmarkScenarioFactory.CreateDescriptors(operationCount);
        var app = BlueprintAppBenchmarkScenarioFactory.CreateRegisteredApp(descriptors);
        var executor = new StressBenchmarkExecutor(app, descriptors);
        var targetDescriptor = descriptors[operationCount / 2];

        var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new Task<bool>[dispatchCount];

        for (var i = 0; i < dispatchCount; i++)
        {
            tasks[i] = DispatchOnceAsync(app, executor, targetDescriptor, startGate.Task);
        }

        startGate.SetResult(true);
        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(static success => success, "all dispatches should complete successfully");
    }

    [Fact]
    public async Task When_512_concurrent_dispatches_against_250_operation_app_then_all_complete_without_faults()
    {
        const int dispatchCount = 512;
        const int operationCount = 250;

        var descriptors = BlueprintAppBenchmarkScenarioFactory.CreateDescriptors(operationCount);
        var app = BlueprintAppBenchmarkScenarioFactory.CreateRegisteredApp(descriptors);
        var executor = new StressBenchmarkExecutor(app, descriptors);
        var targetDescriptor = descriptors[operationCount / 2];

        var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new Task<bool>[dispatchCount];

        for (var i = 0; i < dispatchCount; i++)
        {
            tasks[i] = DispatchOnceAsync(app, executor, targetDescriptor, startGate.Task);
        }

        startGate.SetResult(true);
        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(static success => success, "all dispatches should complete successfully");
    }

    [Fact]
    public void When_openapi_rendered_for_100_operations_then_completes_within_performance_budget()
    {
        var app = CreateHttpExposedApp(operationCount: 100);

        // Warm up
        RenderOpenApiDocument(app);

        var stopwatch = Stopwatch.StartNew();
        for (var iteration = 0; iteration < 10; iteration++)
        {
            RenderOpenApiDocument(app);
        }

        stopwatch.Stop();

        // 10 renders of a 100-operation OpenAPI doc should stay under 5 seconds even on slow CI
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "OpenAPI generation for 100 operations should not be prohibitively slow");
    }

    [Fact]
    public void When_registration_performed_for_250_operations_then_lookup_by_every_registered_type_succeeds()
    {
        var descriptors = BlueprintAppBenchmarkScenarioFactory.CreateDescriptors(operationCount: 250);
        var app = BlueprintAppBenchmarkScenarioFactory.CreateRegisteredApp(descriptors);

        foreach (var descriptor in descriptors)
        {
            app.TryFindOperation(descriptor.OperationType, out var found).Should().BeTrue(
                $"operation {descriptor.Name} should be findable after registration");
            found!.Name.Should().Be(descriptor.Name);
        }
    }

    private static string RenderOpenApiDocument(BlueprintApp app)
    {
        var documentBuilderContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaProjectionContext = OpenApiSchemaProjectionContext.Create(documentBuilderContext);
        var responseSchemaProjectionContext = OpenApiResponseSchemaReferenceProjectionContext.Create(schemaProjectionContext);
        var responseObjectContext = OpenApiResponseObjectRenderingContext.Create(responseSchemaProjectionContext);
        var operationContext = OpenApiOperationRenderingContext.Create(responseObjectContext);
        var pathItemContext = OpenApiPathItemRenderingContext.Create(operationContext);
        var documentContext = OpenApiDocumentRenderingContext.Create(pathItemContext);

        return OpenApiOutputRenderingContext.Create(documentContext, infoOptions: null, schemaProcessorOptions: null).Json;
    }

    /// <summary>
    /// Creates a synthetic app with HTTP host exposure and method metadata so OpenAPI rendering works.
    /// </summary>
    private static BlueprintApp CreateHttpExposedApp(int operationCount)
    {
        var descriptors = BlueprintAppBenchmarkScenarioFactory.CreateDescriptors(operationCount);
        var httpHostExposure = new OperationHostExposureFeatureData(ImmutableArray.Create("http"));
        var httpMethodGet = new OperationHttpMethodMetadataFeatureData(ImmutableArray.Create("GET"));

        foreach (var descriptor in descriptors)
        {
            descriptor.SetFeatureData(httpHostExposure);
            descriptor.SetFeatureData(httpMethodGet);
        }

        return BlueprintAppBenchmarkScenarioFactory.CreateRegisteredApp(descriptors);
    }

    private static async Task<bool> DispatchOnceAsync(
        BlueprintApp app,
        BlueprintAppExecutor executor,
        ApiOperationDescriptor descriptor,
        Task startSignal)
    {
        await startSignal;

        var context = new ApiOperationContext(
            StressEmptyServiceProvider.Instance,
            app,
            descriptor,
            CancellationToken.None);

        var result = await executor.ExecuteAsync(context);
        return result is not null;
    }

    private sealed class StressEmptyServiceProvider : IServiceProvider
    {
        public static StressEmptyServiceProvider Instance { get; } = new();

        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}

/// <summary>
/// Registers a shared no-op pipeline for every descriptor to isolate concurrency stress from handler cost.
/// </summary>
internal sealed class StressBenchmarkExecutor : BlueprintAppExecutor
{
    private static readonly IOperationExecutorPipeline SharedPipeline = new StressNoOpPipeline();

    public StressBenchmarkExecutor(BlueprintApp app, IReadOnlyList<ApiOperationDescriptor> descriptors)
        : base(app)
    {
        foreach (var descriptor in descriptors)
        {
            this.Register(descriptor.OperationType, SharedPipeline);
        }
    }
}

/// <summary>
/// Minimal pipeline body for stress testing executor dispatch concurrency without handler overhead.
/// </summary>
internal sealed class StressNoOpPipeline : IOperationExecutorPipeline
{
    private static readonly Task<OperationResult> CompletedTask =
        Task.FromResult<OperationResult>(StressCompletedResult.Instance);

    public Task<OperationResult> ExecuteAsync(ApiOperationContext context)
    {
        return CompletedTask;
    }
}

internal sealed class StressCompletedResult : OperationResult
{
    private StressCompletedResult()
    {
    }

    public static StressCompletedResult Instance { get; } = new();

    public override Task ExecuteAsync(ApiOperationContext context)
    {
        return Task.CompletedTask;
    }
}

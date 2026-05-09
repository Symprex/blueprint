using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Benchmarks.RuntimeModel;

namespace Blueprint.SourceGenerator.Benchmarks.RuntimeExec;

/// <summary>
/// Measures the shared executor shell separately from direct pipeline execution so request-path cost can be compared
/// against the existing Blueprint app and endpoint-mapping baselines.
/// </summary>
[Config(typeof(InProcessBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class ExecutorDispatchBenchmarks
{
    private BlueprintApp _app = null!;
    private BenchmarkBlueprintAppExecutor _executor = null!;
    private ApiOperationDescriptor _descriptor = null!;
    private IOperationExecutorPipeline _pipeline = null!;

    /// <summary>
    /// Gets or sets how many synthetic operations are registered before measuring dispatch.
    /// </summary>
    [Params(10, 100, 250, 500)]
    public int OperationCount { get; set; }

    /// <summary>
    /// Creates the registered app, executor, and representative descriptor used by the dispatch benchmarks.
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        var descriptors = BlueprintAppBenchmarkScenarioFactory.CreateDescriptors(this.OperationCount);

        this._descriptor = descriptors[^1];
        this._app = BlueprintAppBenchmarkScenarioFactory.CreateRegisteredApp(descriptors);
        this._executor = new BenchmarkBlueprintAppExecutor(this._app, descriptors);
        this._pipeline = this._executor.GetPipeline(this._descriptor.OperationType);
    }

    /// <summary>
    /// Measures the registered pipeline lookup that every executor dispatch must perform before invoking pipeline code.
    /// </summary>
    /// <returns><see langword="true"/> when the resolved pipeline is the expected shared benchmark pipeline instance.</returns>
    [Benchmark]
    public bool ResolveRegisteredPipeline()
    {
        return ReferenceEquals(this._executor.GetPipeline(this._descriptor.OperationType), this._pipeline);
    }

    /// <summary>
    /// Measures the representative pipeline body in isolation so the executor shell overhead has a direct baseline.
    /// </summary>
    /// <returns>The result produced by executing the representative pipeline directly.</returns>
    [Benchmark(Baseline = true)]
    public Task<OperationResult> ExecutePipelineDirectAsync()
    {
        return this._pipeline.ExecuteAsync(CreateContext(this._app, this._descriptor));
    }

    /// <summary>
    /// Measures the end-to-end shared executor shell that resolves a pipeline from the registered app model and executes it.
    /// </summary>
    /// <returns>The result produced by dispatching through <see cref="BlueprintAppExecutor"/>.</returns>
    [Benchmark]
    public Task<OperationResult> ExecuteThroughExecutorShellAsync()
    {
        return this._executor.ExecuteAsync(CreateContext(this._app, this._descriptor));
    }

    private static ApiOperationContext CreateContext(BlueprintApp app, ApiOperationDescriptor descriptor)
    {
        return new ApiOperationContext(EmptyServiceProvider.Instance, app, descriptor, CancellationToken.None);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static EmptyServiceProvider Instance { get; } = new();

        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}

/// <summary>
/// Registers one shared representative pipeline for every synthetic descriptor so dispatch benchmarks isolate executor cost.
/// </summary>
internal sealed class BenchmarkBlueprintAppExecutor : BlueprintAppExecutor
{
    private static readonly IOperationExecutorPipeline SharedPipeline = new NoOpBenchmarkPipeline();

    /// <summary>
    /// Initializes a benchmark executor by registering the synthetic descriptor set against the shared representative pipeline.
    /// </summary>
    /// <param name="app">The benchmark app whose descriptors already match the requested operation count.</param>
    /// <param name="descriptors">The synthetic descriptors that the benchmark should dispatch through.</param>
    public BenchmarkBlueprintAppExecutor(BlueprintApp app, IReadOnlyList<ApiOperationDescriptor> descriptors)
        : base(app)
    {
        foreach (var descriptor in descriptors)
        {
            this.Register(descriptor.OperationType, SharedPipeline);
        }
    }
}

/// <summary>
/// Provides the smallest pipeline body that still exercises the normal executor-to-pipeline contract.
/// </summary>
internal sealed class NoOpBenchmarkPipeline : IOperationExecutorPipeline
{
    private static readonly Task<OperationResult> CompletedResultTask = Task.FromResult<OperationResult>(BenchmarkCompletedResult.Instance);

    /// <inheritdoc />
    public Task<OperationResult> ExecuteAsync(ApiOperationContext context)
    {
        return CompletedResultTask;
    }
}

/// <summary>
/// Represents a completed benchmark result whose execution is intentionally a no-op.
/// </summary>
internal sealed class BenchmarkCompletedResult : OperationResult
{
    private BenchmarkCompletedResult()
    {
    }

    /// <summary>
    /// Gets the shared benchmark result instance reused by the no-op benchmark pipeline.
    /// </summary>
    public static BenchmarkCompletedResult Instance { get; } = new();

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        return Task.CompletedTask;
    }
}

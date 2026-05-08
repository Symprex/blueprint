using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Blueprint.Model;
using Blueprint.SourceGenerator.Benchmarks.RuntimeModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Benchmarks.RuntimeHttp;

/// <summary>
/// Measures the cost of mapping Blueprint-generated endpoints for varying operation counts.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class BlueprintEndpointMappingBenchmarks
{
    private IReadOnlyList<ApiOperationDescriptor> _descriptors = [];
    private IServiceProvider _serviceProvider = null!;

    /// <summary>
    /// Gets or sets how many synthetic operation descriptors are mapped into the benchmark endpoint builder.
    /// </summary>
    [Params(10, 100, 250, 500)]
    public int OperationCount { get; set; }

    /// <summary>
    /// Creates the shared service provider and descriptor set used by the endpoint-mapping benchmark.
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        this._serviceProvider = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        this._descriptors = BlueprintAppBenchmarkScenarioFactory.CreateDescriptors(this.OperationCount);
    }

    /// <summary>
    /// Measures endpoint discovery and route mapping for the prepared descriptor set.
    /// </summary>
    /// <returns>The total number of mapped endpoints produced by the Blueprint API registration.</returns>
    [Benchmark]
    public int MapBlueprintApi()
    {
        BenchmarkBlueprintEndpointMappingApp.Descriptors = this._descriptors;

        var endpoints = new BenchmarkEndpointRouteBuilder(this._serviceProvider);

        endpoints.MapBlueprintApi<BenchmarkBlueprintEndpointMappingApp, global::Blueprint.BlueprintAppExecutor>("api/");

        return endpoints.DataSources.Sum(static dataSource => dataSource.Endpoints.Count);
    }
}

/// <summary>
/// Minimal benchmark app that registers the prepared descriptor set during construction.
/// </summary>
internal sealed class BenchmarkBlueprintEndpointMappingApp : BlueprintApp
{
    /// <summary>
    /// Gets or sets the descriptors to register when the benchmark app instance is created.
    /// </summary>
    public static IReadOnlyList<ApiOperationDescriptor> Descriptors { get; set; } = [];

    /// <summary>
    /// Initializes a benchmark app by registering the descriptor set prepared by the benchmark fixture.
    /// </summary>
    public BenchmarkBlueprintEndpointMappingApp()
    {
        foreach (var descriptor in Descriptors)
        {
            this.Register(descriptor);
        }
    }
}

/// <summary>
/// Lightweight route builder used to measure Blueprint endpoint mapping without hosting a full ASP.NET Core app.
/// </summary>
internal sealed class BenchmarkEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
{
    /// <inheritdoc />
    public ICollection<EndpointDataSource> DataSources { get; } = [];

    /// <inheritdoc />
    public IServiceProvider ServiceProvider { get; } = serviceProvider;

    /// <inheritdoc />
    public IApplicationBuilder CreateApplicationBuilder()
    {
        return new ApplicationBuilder(this.ServiceProvider);
    }
}

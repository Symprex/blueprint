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

[MemoryDiagnoser]
[ShortRunJob]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class BlueprintEndpointMappingBenchmarks
{
    private IReadOnlyList<ApiOperationDescriptor> _descriptors = [];
    private IServiceProvider _serviceProvider = null!;

    [Params(10, 100, 250)]
    public int OperationCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        this._serviceProvider = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        this._descriptors = BlueprintAppBenchmarkScenarioFactory.CreateDescriptors(this.OperationCount);
    }

    [Benchmark]
    public int MapBlueprintApi()
    {
        BenchmarkBlueprintEndpointMappingApp.Descriptors = this._descriptors;

        var endpoints = new BenchmarkEndpointRouteBuilder(this._serviceProvider);

        endpoints.MapBlueprintApi<BenchmarkBlueprintEndpointMappingApp, global::Blueprint.BlueprintAppExecutor>("api/");

        return endpoints.DataSources.Sum(static dataSource => dataSource.Endpoints.Count);
    }
}

internal sealed class BenchmarkBlueprintEndpointMappingApp : BlueprintApp
{
    public static IReadOnlyList<ApiOperationDescriptor> Descriptors { get; set; } = [];

    public BenchmarkBlueprintEndpointMappingApp()
    {
        foreach (var descriptor in Descriptors)
        {
            this.Register(descriptor);
        }
    }
}

internal sealed class BenchmarkEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
{
    public ICollection<EndpointDataSource> DataSources { get; } = [];

    public IServiceProvider ServiceProvider { get; } = serviceProvider;

    public IApplicationBuilder CreateApplicationBuilder()
    {
        return new ApplicationBuilder(this.ServiceProvider);
    }
}

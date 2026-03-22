using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Blueprint.Model;
using BlueprintAppModel = Blueprint.Model.BlueprintApp;

namespace Blueprint.SourceGenerator.Benchmarks.RuntimeModel;

[MemoryDiagnoser]
[ShortRunJob]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class BlueprintAppRegistrationBenchmarks
{
    private IReadOnlyList<ApiOperationDescriptor> _descriptors = [];

    [Params(10, 100, 250)]
    public int OperationCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        this._descriptors = BlueprintAppBenchmarkScenarioFactory.CreateDescriptors(this.OperationCount);
    }

    [Benchmark]
    public BlueprintAppModel RegisterAllOperations()
    {
        return BlueprintAppBenchmarkScenarioFactory.CreateRegisteredApp(this._descriptors);
    }
}

[MemoryDiagnoser]
[ShortRunJob]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class BlueprintAppLookupBenchmarks
{
    private BlueprintAppModel _app = null!;
    private Type _registeredOperationType = null!;

    [Params(10, 100, 250)]
    public int OperationCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var descriptors = BlueprintAppBenchmarkScenarioFactory.CreateDescriptors(this.OperationCount);

        this._registeredOperationType = descriptors[^1].OperationType;
        this._app = BlueprintAppBenchmarkScenarioFactory.CreateRegisteredApp(descriptors);
    }

    [Benchmark(Baseline = true)]
    public bool TryFindRegisteredOperation()
    {
        return this._app.TryFindOperation(this._registeredOperationType, out _);
    }

    [Benchmark]
    public bool TryFindMissingOperation()
    {
        return this._app.TryFindOperation(typeof(MissingBenchmarkOperation), out _);
    }
}

internal static class BlueprintAppBenchmarkScenarioFactory
{
    private const string DescriptorSource = "Benchmark";
    private static readonly Type[] MarkerTypes =
    [
        typeof(Marker00),
        typeof(Marker01),
        typeof(Marker02),
        typeof(Marker03),
        typeof(Marker04),
        typeof(Marker05),
        typeof(Marker06),
        typeof(Marker07),
        typeof(Marker08),
        typeof(Marker09),
        typeof(Marker10),
        typeof(Marker11),
        typeof(Marker12),
        typeof(Marker13),
        typeof(Marker14),
        typeof(Marker15),
    ];

    public static IReadOnlyList<ApiOperationDescriptor> CreateDescriptors(int operationCount)
    {
        var maxOperationCount = MarkerTypes.Length * MarkerTypes.Length;

        if (operationCount < 1 || operationCount > maxOperationCount)
        {
            throw new ArgumentOutOfRangeException(nameof(operationCount), $"Operation count must be between 1 and {maxOperationCount}.");
        }

        var descriptors = new List<ApiOperationDescriptor>(operationCount);

        for (var index = 0; index < operationCount; index++)
        {
            var operationType = typeof(BenchmarkOperation<,>).MakeGenericType(
                MarkerTypes[index / MarkerTypes.Length],
                MarkerTypes[index % MarkerTypes.Length]);
            var descriptor = new ApiOperationDescriptor(operationType, $"Operation{index:D3}", DescriptorSource);

            descriptor.AddUrl(new ApiOperationUrl($"/benchmark/{index:D3}"));
            descriptors.Add(descriptor);
        }

        return descriptors;
    }

    public static BlueprintAppModel CreateRegisteredApp(IReadOnlyList<ApiOperationDescriptor> descriptors)
    {
        var app = new BlueprintAppModel();

        foreach (var descriptor in descriptors)
        {
            app.Register(descriptor);
        }

        return app;
    }
}

// The marker cartesian product gives the benchmark deterministic, unique operation types
// without hand-authoring hundreds of concrete operation classes.
public class BenchmarkOperation<TPrimaryMarker, TSecondaryMarker>;

public class MissingBenchmarkOperation;

public class Marker00;

public class Marker01;

public class Marker02;

public class Marker03;

public class Marker04;

public class Marker05;

public class Marker06;

public class Marker07;

public class Marker08;

public class Marker09;

public class Marker10;

public class Marker11;

public class Marker12;

public class Marker13;

public class Marker14;

public class Marker15;

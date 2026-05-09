using System;
using System.Collections.Generic;
using System.IO;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Blueprint.Model;
using Blueprint.SourceGenerator.Tests.Scale;
using BlueprintAppModel = Blueprint.Model.BlueprintApp;

namespace Blueprint.SourceGenerator.Benchmarks.RuntimeModel;

/// <summary>
/// Measures the cost of registering batches of generated operation descriptors into a fresh <see cref="BlueprintAppModel"/>.
/// </summary>
[Config(typeof(InProcessBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class BlueprintAppRegistrationBenchmarks
{
    /// <summary>
    /// Caches the descriptor set reused by the registration benchmark for the active parameter value.
    /// </summary>
    private IReadOnlyList<ApiOperationDescriptor> _descriptors = [];

    /// <summary>
    /// Gets or sets how many synthetic operation descriptors the setup phase prepares for each benchmark iteration.
    /// </summary>
    [Params(10, 100, 250, 500)]
    public int OperationCount { get; set; }

    /// <summary>
    /// Builds the deterministic descriptor set used by the registration scenario.
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        this._descriptors = BlueprintAppBenchmarkScenarioFactory.CreateDescriptors(this.OperationCount);
    }

    /// <summary>
    /// Measures the cost of creating a new app and registering every prepared descriptor.
    /// </summary>
    /// <returns>The populated app instance produced by the registration run.</returns>
    [Benchmark]
    public BlueprintAppModel RegisterAllOperations()
    {
        return BlueprintAppBenchmarkScenarioFactory.CreateRegisteredApp(this._descriptors);
    }
}

/// <summary>
/// Measures the cost of locating registered and missing operations in a populated <see cref="BlueprintAppModel"/>.
/// </summary>
[Config(typeof(InProcessBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class BlueprintAppLookupBenchmarks
{
    /// <summary>
    /// Caches the populated app shared by the lookup scenarios for the current benchmark parameter.
    /// </summary>
    private BlueprintAppModel _app = null!;

    /// <summary>
    /// Stores a known registered operation type so the positive lookup benchmark avoids setup work during measurement.
    /// </summary>
    private Type _registeredOperationType = null!;

    /// <summary>
    /// Gets or sets how many synthetic operations are registered before running the lookup scenarios.
    /// </summary>
    [Params(10, 100, 250, 500)]
    public int OperationCount { get; set; }

    /// <summary>
    /// Creates the registered app and captures one known operation type for the positive lookup path.
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        var descriptors = BlueprintAppBenchmarkScenarioFactory.CreateDescriptors(this.OperationCount);

        this._registeredOperationType = descriptors[^1].OperationType;
        this._app = BlueprintAppBenchmarkScenarioFactory.CreateRegisteredApp(descriptors);
    }

    /// <summary>
    /// Measures the cost of locating an operation that is already registered in the app.
    /// </summary>
    /// <returns><see langword="true"/> when the app finds the known registered operation.</returns>
    [Benchmark(Baseline = true)]
    public bool TryFindRegisteredOperation()
    {
        return this._app.TryFindOperation(this._registeredOperationType, out _);
    }

    /// <summary>
    /// Measures the cost of a lookup that misses because the requested operation was never registered.
    /// </summary>
    /// <returns><see langword="true"/> when the missing operation unexpectedly exists; otherwise, <see langword="false"/>.</returns>
    [Benchmark]
    public bool TryFindMissingOperation()
    {
        return this._app.TryFindOperation(typeof(MissingBenchmarkOperation), out _);
    }
}

/// <summary>
/// Creates deterministic descriptor and app fixtures so the Blueprint app benchmarks compare identical operation sets.
/// </summary>
internal static class BlueprintAppBenchmarkScenarioFactory
{
    /// <summary>
    /// Identifies the synthetic descriptor source used throughout the benchmark fixtures.
    /// </summary>
    private const string DescriptorSource = "Benchmark";

    /// <summary>
    /// Keeps the runtime benchmark fixture namespace aligned with the shared synthetic source generator.
    /// </summary>
    private const string FixtureNamespaceName = "Synthetic.Benchmark";

    /// <summary>
    /// Keeps the runtime benchmark fixture app name aligned with the shared synthetic source generator.
    /// </summary>
    private const string FixtureAppName = "SyntheticBenchmarkApp";

    /// <summary>
    /// Supplies the marker types whose Cartesian product produces a large set of unique synthetic operations.
    /// </summary>
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
        typeof(Marker16),
        typeof(Marker17),
        typeof(Marker18),
        typeof(Marker19),
        typeof(Marker20),
        typeof(Marker21),
        typeof(Marker22),
    ];

    /// <summary>
    /// Creates a deterministic set of operation descriptors for the requested benchmark size.
    /// </summary>
    /// <param name="operationCount">The number of synthetic operations to describe.</param>
    /// <returns>A descriptor list whose operation identities and URLs are stable across runs.</returns>
    public static IReadOnlyList<ApiOperationDescriptor> CreateDescriptors(int operationCount)
    {
        var maxOperationCount = MarkerTypes.Length * MarkerTypes.Length;

        if (operationCount < 1 || operationCount > maxOperationCount)
        {
            throw new ArgumentOutOfRangeException(nameof(operationCount), $"Operation count must be between 1 and {maxOperationCount}.");
        }

        var fixture = SyntheticBlueprintFixtureGenerator.Create(
            new SyntheticBlueprintFixtureOptions(
                namespaceName: FixtureNamespaceName,
                appName: FixtureAppName,
                operationCount: operationCount));
        var operationSourceFiles = GetOperationSourceFiles(fixture);
        var descriptors = new List<ApiOperationDescriptor>(operationCount);

        for (var index = 0; index < operationCount; index++)
        {
            var operationType = typeof(BenchmarkOperation<,>).MakeGenericType(
                MarkerTypes[index / MarkerTypes.Length],
                MarkerTypes[index % MarkerTypes.Length]);
            var operationName = Path.GetFileNameWithoutExtension(operationSourceFiles[index].Path);
            var descriptor = new ApiOperationDescriptor(operationType, operationName, DescriptorSource);

            descriptor.AddUrl(new ApiOperationUrl($"/synthetic/operations/{index + 1:D3}"));
            descriptors.Add(descriptor);
        }

        return descriptors;
    }

    /// <summary>
    /// Creates a new app instance and registers each supplied descriptor in order.
    /// </summary>
    /// <param name="descriptors">The descriptors to register into the app.</param>
    /// <returns>A populated app ready for lookup benchmarks.</returns>
    public static BlueprintAppModel CreateRegisteredApp(IReadOnlyList<ApiOperationDescriptor> descriptors)
    {
        var app = new BlueprintAppModel();

        foreach (var descriptor in descriptors)
        {
            app.Register(descriptor);
        }

        return app;
    }

    private static List<SyntheticBlueprintFixtureSourceFile> GetOperationSourceFiles(SyntheticBlueprintFixture fixture)
    {
        var sourceFiles = new List<SyntheticBlueprintFixtureSourceFile>(fixture.OperationCount);

        foreach (var sourceFile in fixture.SourceFiles)
        {
            if (sourceFile.Path.StartsWith(@"Operations\", StringComparison.Ordinal))
            {
                sourceFiles.Add(sourceFile);
            }
        }

        return sourceFiles;
    }
}

/// <summary>
/// Represents one synthetic benchmark operation generated from the Cartesian product of marker types.
/// </summary>
/// <typeparam name="TPrimaryMarker">The primary marker that helps create a unique CLR type identity.</typeparam>
/// <typeparam name="TSecondaryMarker">The secondary marker that expands the synthetic operation set.</typeparam>
public class BenchmarkOperation<TPrimaryMarker, TSecondaryMarker>;

/// <summary>
/// Represents an operation type that is intentionally absent from the benchmark app so lookup misses can be measured.
/// </summary>
public class MissingBenchmarkOperation;

/// <summary>
/// Marker type 00 used to synthesize a unique benchmark operation identity.
/// </summary>
public class Marker00;

/// <summary>
/// Marker type 01 used to synthesize a unique benchmark operation identity.
/// </summary>
public class Marker01;

/// <summary>
/// Marker type 02 used to synthesize a unique benchmark operation identity.
/// </summary>
public class Marker02;

/// <summary>
/// Marker type 03 used to synthesize a unique benchmark operation identity.
/// </summary>
public class Marker03;

/// <summary>
/// Marker type 04 used to synthesize a unique benchmark operation identity.
/// </summary>
public class Marker04;

/// <summary>
/// Marker type 05 used to synthesize a unique benchmark operation identity.
/// </summary>
public class Marker05;

/// <summary>
/// Marker type 06 used to synthesize a unique benchmark operation identity.
/// </summary>
public class Marker06;

/// <summary>
/// Marker type 07 used to synthesize a unique benchmark operation identity.
/// </summary>
public class Marker07;

/// <summary>
/// Marker type 08 used to synthesize a unique benchmark operation identity.
/// </summary>
public class Marker08;

/// <summary>
/// Marker type 09 used to synthesize a unique benchmark operation identity.
/// </summary>
public class Marker09;

/// <summary>
/// Marker type 10 used to synthesize a unique benchmark operation identity.
/// </summary>
public class Marker10;

/// <summary>
/// Marker type 11 used to synthesize a unique benchmark operation identity.
/// </summary>
public class Marker11;

/// <summary>
/// Marker type 12 used to synthesize a unique benchmark operation identity.
/// </summary>
public class Marker12;

/// <summary>
/// Marker type 13 used to synthesize a unique benchmark operation identity.
/// </summary>
public class Marker13;

/// <summary>
/// Marker type 14 used to synthesize a unique benchmark operation identity.
/// </summary>
public class Marker14;

/// <summary>
/// Marker type 15 used to synthesize a unique benchmark operation identity.
/// </summary>
public class Marker15;

/// <summary>
/// Marker type 16 used to synthesize a unique benchmark operation identity.
/// </summary>
public class Marker16;

/// <summary>
/// Marker type 17 used to synthesize a unique benchmark operation identity.
/// </summary>
public class Marker17;

/// <summary>
/// Marker type 18 used to synthesize a unique benchmark operation identity.
/// </summary>
public class Marker18;

/// <summary>
/// Marker type 19 used to synthesize a unique benchmark operation identity.
/// </summary>
public class Marker19;

/// <summary>
/// Marker type 20 used to synthesize a unique benchmark operation identity.
/// </summary>
public class Marker20;

/// <summary>
/// Marker type 21 used to synthesize a unique benchmark operation identity.
/// </summary>
public class Marker21;

/// <summary>
/// Marker type 22 used to synthesize a unique benchmark operation identity.
/// </summary>
public class Marker22;

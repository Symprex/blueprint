using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Blueprint.SourceGenerator.Generators.App;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Tests.Scale;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Benchmarks.Scale;

/// <summary>
/// Measures the source generator's compile-time performance across different app sizes.
/// Benchmarks both initial and incremental (cached) generator runs to quantify how generator
/// cost scales with operation count and how effective the incremental pipeline cache is.
/// </summary>
[Config(typeof(InProcessBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class SourceGeneratorCompilationBenchmarks
{
    private static readonly CSharpParseOptions _parseOptions = new(LanguageVersion.Preview);
    private static readonly List<MetadataReference> _metadataReferences = CreateMetadataReferences();

    private SyntheticBlueprintFixture _fixture10 = null!;
    private SyntheticBlueprintFixture _fixture50 = null!;
    private SyntheticBlueprintFixture _fixture100 = null!;
    private SyntheticBlueprintFixture _fixture250 = null!;
    private SyntheticBlueprintFixture _fixture500 = null!;

    private CSharpCompilation _compilation10 = null!;
    private CSharpCompilation _compilation50 = null!;
    private CSharpCompilation _compilation100 = null!;
    private CSharpCompilation _compilation250 = null!;
    private CSharpCompilation _compilation500 = null!;

    /// <summary>
    /// Pre-builds the synthetic fixtures and Roslyn compilations so benchmark iterations
    /// measure only generator execution, not fixture or compilation setup.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        this._fixture10 = SyntheticBlueprintFixtureGenerator.Create(
            new SyntheticBlueprintFixtureOptions("Bench10", "Bench10App", 10, includeRootLinks: true));
        this._fixture50 = SyntheticBlueprintFixtureGenerator.Create(
            new SyntheticBlueprintFixtureOptions("Bench50", "Bench50App", 50, includeRootLinks: true));
        this._fixture100 = SyntheticBlueprintFixtureGenerator.Create(
            new SyntheticBlueprintFixtureOptions("Bench100", "Bench100App", 100, includeRootLinks: true));
        this._fixture250 = SyntheticBlueprintFixtureGenerator.Create(
            new SyntheticBlueprintFixtureOptions("Bench250", "Bench250App", 250, includeRootLinks: true));
        this._fixture500 = SyntheticBlueprintFixtureGenerator.Create(
            new SyntheticBlueprintFixtureOptions("Bench500", "Bench500App", 500, includeRootLinks: true));

        this._compilation10 = CreateCompilation(this._fixture10);
        this._compilation50 = CreateCompilation(this._fixture50);
        this._compilation100 = CreateCompilation(this._fixture100);
        this._compilation250 = CreateCompilation(this._fixture250);
        this._compilation500 = CreateCompilation(this._fixture500);
    }

    // ─── Initial (cold) runs ─────────────────────────────────────────────

    /// <summary>
    /// Measures initial (cold) generation cost for a 10-operation app — the smallest
    /// realistic size, establishing the generator's fixed overhead.
    /// </summary>
    /// <returns>The generator driver after running all generators (prevents dead-code elimination).</returns>
    [Benchmark(Baseline = true)]
    public GeneratorDriver InitialRun_10Ops()
    {
        return RunAllGenerators(this._compilation10);
    }

    /// <summary>
    /// Measures initial generation cost for a 50-operation app — typical small service size.
    /// </summary>
    /// <returns>The generator driver after running all generators.</returns>
    [Benchmark]
    public GeneratorDriver InitialRun_50Ops()
    {
        return RunAllGenerators(this._compilation50);
    }

    /// <summary>
    /// Measures initial generation cost for a 100-operation app — S365 medium service size.
    /// </summary>
    /// <returns>The generator driver after running all generators.</returns>
    [Benchmark]
    public GeneratorDriver InitialRun_100Ops()
    {
        return RunAllGenerators(this._compilation100);
    }

    /// <summary>
    /// Measures initial generation cost for a 250-operation app — S365 largest service size
    /// (Portal has ~70 operations, so 250 provides headroom for future growth).
    /// </summary>
    /// <returns>The generator driver after running all generators.</returns>
    [Benchmark]
    public GeneratorDriver InitialRun_250Ops()
    {
        return RunAllGenerators(this._compilation250);
    }

    /// <summary>
    /// Measures initial generation cost for a 500-operation app — simulates S365's total
    /// operation count across all services (~540 ops), validating generator performance at
    /// the largest realistic compilation unit.
    /// </summary>
    /// <returns>The generator driver after running all generators.</returns>
    [Benchmark]
    public GeneratorDriver InitialRun_500Ops()
    {
        return RunAllGenerators(this._compilation500);
    }

    // ─── Incremental (cached) runs ───────────────────────────────────────

    private GeneratorDriver _warmedDriver100 = null!;

    /// <summary>
    /// Warms the generator driver with an initial run so the incremental benchmark
    /// measures only the cache-hit path.
    /// </summary>
    [IterationSetup(Target = nameof(IncrementalCachedRun_100Ops))]
    public void SetupIncrementalCachedRun100()
    {
        this._warmedDriver100 = RunAllGenerators(this._compilation100);
    }

    /// <summary>
    /// Measures incremental re-run cost for a 100-operation app when nothing has changed.
    /// The initial run is performed in <see cref="SetupIncrementalCachedRun100"/> so this
    /// benchmark isolates only the cache-hit path.
    /// </summary>
    /// <returns>The generator driver after the cached re-run.</returns>
    [Benchmark]
    public GeneratorDriver IncrementalCachedRun_100Ops()
    {
        return this._warmedDriver100.RunGenerators(this._compilation100.Clone());
    }

    // ─── Individual generator benchmarks ─────────────────────────────────

    /// <summary>
    /// Measures the descriptor generator alone for 100 operations — isolates the cost
    /// of generating ApiOperationDescriptor Configure() methods.
    /// </summary>
    /// <returns>The generator driver after running the descriptor generator.</returns>
    [Benchmark]
    public GeneratorDriver DescriptorOnly_100Ops()
    {
        return RunSingleGenerator<OperationDescriptorSourceGenerator>(this._compilation100);
    }

    /// <summary>
    /// Measures the pipeline generator alone for 100 operations — isolates the cost
    /// of generating executor pipeline methods.
    /// </summary>
    /// <returns>The generator driver after running the pipeline generator.</returns>
    [Benchmark]
    public GeneratorDriver PipelineOnly_100Ops()
    {
        return RunSingleGenerator<OperationExecutorPipelineSourceGenerator>(this._compilation100);
    }

    /// <summary>
    /// Measures the app registration generator alone for 100 operations — isolates the
    /// cost of generating the BlueprintApp partial with operation registrations.
    /// </summary>
    /// <returns>The generator driver after running the app registration generator.</returns>
    [Benchmark]
    public GeneratorDriver AppRegistration_100Ops()
    {
        return RunSingleGenerator<AppDataModelSourceGenerator>(this._compilation100);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static GeneratorDriver RunAllGenerators(CSharpCompilation compilation)
    {
        var descriptorGenerator = new OperationDescriptorSourceGenerator().AsSourceGenerator();
        var pipelineGenerator = new OperationExecutorPipelineSourceGenerator().AsSourceGenerator();
        var appGenerator = new AppDataModelSourceGenerator().AsSourceGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [descriptorGenerator, pipelineGenerator, appGenerator],
            parseOptions: _parseOptions);

        return driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
    }

    private static GeneratorDriver RunSingleGenerator<T>(CSharpCompilation compilation)
        where T : IIncrementalGenerator, new()
    {
        var generator = new T().AsSourceGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [generator],
            parseOptions: _parseOptions);

        return driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
    }

    private static CSharpCompilation CreateCompilation(SyntheticBlueprintFixture fixture)
    {
        var syntaxTrees = fixture.SourceFiles
            .Select(static sf => CSharpSyntaxTree.ParseText(sf.Content, _parseOptions, sf.Path))
            .ToArray();

        return CSharpCompilation.Create(
            $"BenchmarkApp_{fixture.OperationCount}",
            syntaxTrees,
            _metadataReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static List<MetadataReference> CreateMetadataReferences()
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(static a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToList();

        EnsureReference(references, typeof(HttpRequestMessage));
        EnsureReference(references, typeof(System.Net.Http.Headers.AuthenticationHeaderValue));
        EnsureReference(references, typeof(Blueprint.Model.ICommand));
        EnsureReference(references, typeof(Blueprint.OperationResult));
        EnsureReference(references, typeof(Blueprint.Runtime.Exec.ApiOperationContext));
        EnsureReference(references, typeof(System.ComponentModel.DataAnnotations.ValidationResult));
        EnsureReference(references, typeof(Microsoft.Extensions.Logging.ILogger));
        EnsureReference(references, typeof(Microsoft.Extensions.Logging.LoggerMessage));
        EnsureReference(references, typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger));
        EnsureReference(references, typeof(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions));
        EnsureReference(references, typeof(System.Diagnostics.Activity));
        EnsureReference(references, typeof(System.Text.Json.Serialization.JsonSerializerContext));
        EnsureReference(references, typeof(Microsoft.Extensions.Primitives.StringValues));
        EnsureReference(references, typeof(Microsoft.AspNetCore.Http.IQueryCollection));
        EnsureReference(references, typeof(Microsoft.AspNetCore.Http.HttpContext));
        EnsureReference(references, typeof(Blueprint.Rebus.RebusMessageFeature));
        EnsureReference(references, typeof(Blueprint.Marten.MartenScopeAccessor));
        EnsureReference(references, typeof(Blueprint.HttpClient.BlueprintHttpClient<>));

        return references;
    }

    private static void EnsureReference(List<MetadataReference> references, Type type)
    {
        var location = type.Assembly.Location;

        if (string.IsNullOrEmpty(location))
        {
            return;
        }

        if (references.Any(r => r.Display == location))
        {
            return;
        }

        references.Add(MetadataReference.CreateFromFile(location));
    }
}

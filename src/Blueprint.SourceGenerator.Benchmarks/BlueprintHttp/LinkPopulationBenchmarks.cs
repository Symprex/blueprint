using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Benchmarks.RuntimeHttp;

/// <summary>
/// Measures the cost of HATEOAS link population on the pipeline hot path. Compares the inline
/// ApiLinkGenerator construction (from pre-computed BaseUri) against the DI resolution fallback.
/// This benchmark isolates the optimization in commit 34881924 which eliminated per-request
/// scoped DI resolution of IApiLinkGenerator for operations with link generation.
/// </summary>
[Config(typeof(InProcessBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class LinkPopulationBenchmarks
{
    private IServiceProvider _serviceProviderWithLinkGen = null!;
    private IServiceProvider _serviceProviderWithoutLinkGen = null!;
    private ApiOperationContext _contextDiFallback = null!;
    private ApiOperationContext _contextInlinePath = null!;
    private OperationResult _okResult = null!;

    /// <summary>
    /// Sets up service providers and contexts for both paths.
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        var app = new BlueprintApp();

        var descriptor = new ApiOperationDescriptor(
            typeof(LinkPopBenchmarkQuery),
            nameof(LinkPopBenchmarkQuery),
            "Benchmark");

        var link = new ApiOperationLink(
            descriptor,
            "/link-pop/{Id}",
            "/link-pop/{Id}",
            "self",
            typeof(LinkPopBenchmarkResource))
        {
            CompiledUrlBuilder = static obj => obj switch
            {
                LinkPopBenchmarkResource r => "link-pop/" + System.Uri.EscapeDataString(r.Id),
                string id => "link-pop/" + System.Uri.EscapeDataString(id),
                _ => null,
            },
        };

        descriptor.AddUrl(new ApiOperationUrl("/link-pop/{Id}"));
        descriptor.AddLink(link);
        app.Register(descriptor);
        app.Freeze();

        // Service provider with IApiLinkGenerator (the DI fallback path)
        var services1 = new ServiceCollection();
        services1.AddLogging();
        services1.AddSingleton<IApiLinkGenerator>(new ApiLinkGenerator(app, "https://api.example.com/"));
        services1.AddSingleton<IResourceLinkGenerator, BenchLinkGenerator>();
        this._serviceProviderWithLinkGen = services1.BuildServiceProvider();

        // Service provider without IApiLinkGenerator (no link gen registered = skip path)
        var services2 = new ServiceCollection();
        services2.AddLogging();
        services2.AddSingleton<IResourceLinkGenerator, BenchLinkGenerator>();
        this._serviceProviderWithoutLinkGen = services2.BuildServiceProvider();

        // Context without BaseUri uses the DI fallback path
        this._contextDiFallback = new ApiOperationContext(
            this._serviceProviderWithLinkGen,
            app,
            descriptor,
            CancellationToken.None)
        {
            Operation = new LinkPopBenchmarkQuery { Id = "resource-42" },
        };

        // Context with BaseUri uses the cached inline path (new optimization)
        this._contextInlinePath = new ApiOperationContext(
            this._serviceProviderWithLinkGen,
            app,
            descriptor,
            CancellationToken.None)
        {
            Operation = new LinkPopBenchmarkQuery { Id = "resource-42" },
            BaseUri = "https://api.example.com/",
        };

        this._okResult = new OkResult<LinkPopBenchmarkResource>(new LinkPopBenchmarkResource { Id = "resource-42" });
    }

    /// <summary>
    /// Measures the DI fallback path: resolves IApiLinkGenerator from the service provider.
    /// This was the ONLY path before the optimization. In production (with BaseUri set), the
    /// inline path avoids this DI resolution entirely.
    /// </summary>
    /// <returns>A completed task.</returns>
    [Benchmark(Baseline = true)]
    public Task PopulateLinks_DiFallbackPath()
    {
        return PipelineHelpers.PopulateHateoasLinksAsync(
            this._serviceProviderWithLinkGen,
            this._contextDiFallback,
            this._okResult);
    }

    /// <summary>
    /// Measures the cached inline path: BaseUri is set and the ApiLinkGenerator is cached
    /// via reference equality. This is the production hot path — eliminates both DI resolution
    /// and per-request ApiLinkGenerator allocation after the first request.
    /// </summary>
    /// <returns>A completed task.</returns>
    [Benchmark]
    public Task PopulateLinks_CachedInlinePath()
    {
        return PipelineHelpers.PopulateHateoasLinksAsync(
            this._serviceProviderWithLinkGen,
            this._contextInlinePath,
            this._okResult);
    }

    /// <summary>
    /// Measures the skip path: no BaseUri and no IApiLinkGenerator registered.
    /// Link population is skipped entirely — the cheapest possible path.
    /// </summary>
    /// <returns>A completed task.</returns>
    [Benchmark]
    public Task PopulateLinks_SkipPath()
    {
        return PipelineHelpers.PopulateHateoasLinksAsync(
            this._serviceProviderWithoutLinkGen,
            this._contextDiFallback,
            this._okResult);
    }
}

/// <summary>
/// Minimal resource for link population benchmarks.
/// </summary>
public partial class LinkPopBenchmarkResource : ApiResource
{
    /// <summary>Gets or sets the resource ID.</summary>
    public string Id { get; set; } = string.Empty;
}

/// <summary>
/// Placeholder query for link population benchmarks.
/// </summary>
[RootLink("/link-pop/{Id}")]
[AllowAnonymous]
public partial class LinkPopBenchmarkQuery : IQuery<LinkPopBenchmarkResource>
{
    /// <summary>Gets or sets the resource ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Returns a benchmark resource.
    /// </summary>
    /// <param name="context">The operation context.</param>
    /// <returns>A benchmark resource.</returns>
    public Task<LinkPopBenchmarkResource> HandleAsync(ApiOperationContext context)
    {
        return Task.FromResult(new LinkPopBenchmarkResource { Id = this.Id });
    }
}

/// <summary>
/// Simple IResourceLinkGenerator that adds one link per resource.
/// </summary>
internal sealed class BenchLinkGenerator : IResourceLinkGenerator
{
    /// <summary>
    /// Adds an edit link to any <see cref="LinkPopBenchmarkResource"/>.
    /// </summary>
    /// <param name="apiLinkGenerator">The link generator for building URLs.</param>
    /// <param name="context">The operation context.</param>
    /// <param name="linkableResource">The resource to add links to.</param>
    /// <returns>A completed task.</returns>
    public ValueTask AddLinksAsync(IApiLinkGenerator apiLinkGenerator, ApiOperationContext context, ILinkableResource linkableResource)
    {
        if (linkableResource is LinkPopBenchmarkResource resource)
        {
            resource.AddLink("edit", new Link { Href = $"/link-pop/{resource.Id}/edit" });
        }

        return ValueTask.CompletedTask;
    }
}

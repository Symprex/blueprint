using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Benchmarks.RuntimeHttp;

/// <summary>
/// Measures link generation cost across the paths S365 uses most heavily: compiled self-link
/// resolution and reflection-based convenience overloads. Results inform whether the anonymous-object
/// convenience API (<c>CreateSelfLink&lt;T&gt;(string id)</c>) needs a source-generated fast path.
/// </summary>
[Config(typeof(InProcessBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class LinkGenerationBenchmarks
{
    private ApiLinkGenerator _linkGenerator = null!;
    private BenchmarkLinkResource _typedResource = null!;

    /// <summary>
    /// Sets up a minimal <see cref="BlueprintApp"/> with one operation, one self-link with a compiled
    /// URL builder, and an <see cref="ApiLinkGenerator"/> ready to resolve links.
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        var app = new BlueprintApp();

        var descriptor = new ApiOperationDescriptor(
            typeof(BenchmarkGetResourceQuery),
            nameof(BenchmarkGetResourceQuery),
            "Benchmark");

        var link = new ApiOperationLink(
            descriptor,
            "/resources/{Id}",
            "resources/{Id}",
            "self",
            typeof(BenchmarkLinkResource))
        {
            // Compiled URL builder — mirrors what the source generator now emits:
            // a switch expression with resource-type, string, and Guid cases
            CompiledUrlBuilder = static obj => obj switch
            {
                BenchmarkLinkResource r => "resources/" + Uri.EscapeDataString(r.Id),
                string id => "resources/" + Uri.EscapeDataString(id),
                Guid g => $"resources/{g}",
                _ => null,
            },
        };

        descriptor.AddUrl(new ApiOperationUrl("/resources/{Id}"));
        descriptor.AddLink(link);
        app.Register(descriptor);
        app.Freeze();

        this._linkGenerator = new ApiLinkGenerator(app, "https://api.example.com/");
        this._typedResource = new BenchmarkLinkResource { Id = "resource-42" };
    }

    /// <summary>
    /// Compiled path: typed resource passed directly — the <see cref="ApiOperationLink.CompiledUrlBuilder"/>
    /// handles URL construction without reflection. This is the production hot path.
    /// </summary>
    /// <returns>The generated HATEOAS link.</returns>
    [Benchmark(Baseline = true)]
    public Link CreateSelfLink_TypedResource()
    {
        return this._linkGenerator.CreateSelfLink<BenchmarkLinkResource>(this._typedResource);
    }

    /// <summary>
    /// Compiled path: string ID passed directly — the <see cref="ApiOperationLink.CompiledUrlBuilder"/>
    /// matches the <c>string</c> case in its switch expression, using direct string interpolation
    /// without anonymous-object allocation or reflection.
    /// </summary>
    /// <returns>The generated HATEOAS link.</returns>
    [Benchmark]
    public Link CreateSelfLink_StringId()
    {
        return this._linkGenerator.CreateSelfLink<BenchmarkLinkResource>("resource-42");
    }

    /// <summary>
    /// Compiled path: GUID ID is boxed and passed directly to the compiled builder's
    /// <c>Guid</c> case, which formats via <see cref="ISpanFormattable.TryFormat"/> into
    /// the interpolated string — avoids <c>Guid.ToString()</c> heap allocation.
    /// </summary>
    /// <returns>The generated HATEOAS link.</returns>
    [Benchmark]
    public Link CreateSelfLink_GuidId()
    {
        return this._linkGenerator.CreateSelfLink<BenchmarkLinkResource>(Guid.Empty);
    }
}

/// <summary>
/// Minimal API resource for link generation benchmarks.
/// </summary>
public partial class BenchmarkLinkResource : ApiResource
{
    /// <summary>
    /// Gets or sets the resource identifier used in self-link URL templates.
    /// </summary>
    public string Id { get; set; } = string.Empty;
}

/// <summary>
/// Placeholder operation type for the self-link descriptor registration.
/// </summary>
[RootLink("/resources/{Id}")]
[AllowAnonymous]
public partial class BenchmarkGetResourceQuery : IQuery<BenchmarkLinkResource>
{
    /// <summary>
    /// Gets or sets the resource identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Returns a benchmark resource with the requested ID.
    /// </summary>
    /// <param name="context">The operation context.</param>
    /// <returns>A benchmark resource.</returns>
    public Task<BenchmarkLinkResource> HandleAsync(ApiOperationContext context)
    {
        return Task.FromResult(new BenchmarkLinkResource { Id = this.Id });
    }
}

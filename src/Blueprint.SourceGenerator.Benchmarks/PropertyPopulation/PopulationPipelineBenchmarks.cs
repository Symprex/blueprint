using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Benchmarks.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Benchmarks.PropertyPopulation;

/// <summary>
/// Measures generated property population pipeline costs across different binding source profiles:
/// route-only, body-only, mixed route+body, typed properties, and larger payloads.
/// </summary>
[Config(typeof(InProcessBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class PopulationPipelineBenchmarks
{
    private static readonly byte[] ThreeStringBodyBytes = Encoding.UTF8.GetBytes(
        """{"name":"Alice","email":"alice@example.com","department":"Engineering"}""");

    private static readonly byte[] MixedRouteBodyBytes = Encoding.UTF8.GetBytes(
        """{"name":"Alice","email":"alice@example.com"}""");

    private static readonly byte[] TypedBodyBytes = Encoding.UTF8.GetBytes(
        """{"count":42,"active":true,"label":"test","tag":"d3b07384-d9a0-4e9a-8c1a-5f1c0a1b2c3d"}""");

    private static readonly byte[] LargeBodyBytes = Encoding.UTF8.GetBytes(
        """{"field1":"a","field2":"b","field3":"c","field4":"d","field5":"e","field6":"f","field7":"g","field8":"h","field9":"i","field10":"j"}""");

    private readonly ValidationBenchmarksApp _app = new();
    private readonly ValidationBenchmarksAppExecutor _executor;

    /// <summary>
    /// Initializes the benchmark fixture and creates the shared generated executor instance.
    /// </summary>
    public PopulationPipelineBenchmarks()
    {
        this._executor = new ValidationBenchmarksAppExecutor(NullLoggerFactory.Instance, this._app, new PopulationEmptyServiceProvider());
    }

    /// <summary>
    /// Measures route-only extraction for a GET request with one string route parameter and no body.
    /// </summary>
    /// <returns>The operation result produced by executing the measured population pipeline.</returns>
    [Benchmark(Baseline = true)]
    public ValueTask<OperationResult> RouteOnlyGetAsync()
    {
        var context = CreateContext<RouteOnlyGetBenchmarkQuery>(this._app, RouteOnlyGetBenchmarkQuery.Configure());
        SetupHttpContext(context, routeValues: new() { ["Id"] = "user-123" });
        return this._executor.GetPipeline(typeof(RouteOnlyGetBenchmarkQuery)).ExecuteAsync(context);
    }

    /// <summary>
    /// Measures body-only population for a POST with three string properties and no route parameters.
    /// </summary>
    /// <returns>The operation result produced by executing the measured population pipeline.</returns>
    [Benchmark]
    public ValueTask<OperationResult> BodyOnly3StringsPostAsync()
    {
        var context = CreateContext<BodyOnly3StringsBenchmarkCommand>(this._app, BodyOnly3StringsBenchmarkCommand.Configure());
        SetupHttpContext(context, bodyBytes: ThreeStringBodyBytes);
        return this._executor.GetPipeline(typeof(BodyOnly3StringsBenchmarkCommand)).ExecuteAsync(context);
    }

    /// <summary>
    /// Measures the most common real-world pattern: one route parameter plus two body properties.
    /// </summary>
    /// <returns>The operation result produced by executing the measured population pipeline.</returns>
    [Benchmark]
    public ValueTask<OperationResult> MixedRouteAndBodyPostAsync()
    {
        var context = CreateContext<MixedRouteBodyBenchmarkCommand>(this._app, MixedRouteBodyBenchmarkCommand.Configure());
        SetupHttpContext(
            context,
            bodyBytes: MixedRouteBodyBytes,
            routeValues: new() { ["Id"] = "user-456" });
        return this._executor.GetPipeline(typeof(MixedRouteBodyBenchmarkCommand)).ExecuteAsync(context);
    }

    /// <summary>
    /// Measures body population with typed properties (int, bool, string, Guid) plus a route parameter.
    /// </summary>
    /// <returns>The operation result produced by executing the measured population pipeline.</returns>
    [Benchmark]
    public ValueTask<OperationResult> TypedBodyPostAsync()
    {
        var context = CreateContext<TypedBodyBenchmarkCommand>(this._app, TypedBodyBenchmarkCommand.Configure());
        SetupHttpContext(
            context,
            bodyBytes: TypedBodyBytes,
            routeValues: new() { ["Id"] = "cmd-789" });
        return this._executor.GetPipeline(typeof(TypedBodyBenchmarkCommand)).ExecuteAsync(context);
    }

    /// <summary>
    /// Measures body population scaling with 10 string properties.
    /// </summary>
    /// <returns>The operation result produced by executing the measured population pipeline.</returns>
    [Benchmark]
    public ValueTask<OperationResult> LargeBody10PropsPostAsync()
    {
        var context = CreateContext<LargeBodyBenchmarkCommand>(this._app, LargeBodyBenchmarkCommand.Configure());
        SetupHttpContext(context, bodyBytes: LargeBodyBytes);
        return this._executor.GetPipeline(typeof(LargeBodyBenchmarkCommand)).ExecuteAsync(context);
    }

    private static ApiOperationContext CreateContext<TOperation>(
        BlueprintApp app,
        ApiOperationDescriptor descriptor)
        where TOperation : class, IOperation
    {
        return new ApiOperationContext(new PopulationEmptyServiceProvider(), app, descriptor, CancellationToken.None);
    }

    private static void SetupHttpContext(
        ApiOperationContext context,
        byte[]? bodyBytes = null,
        RouteValueDictionary? routeValues = null)
    {
        var httpContext = new DefaultHttpContext();

        if (bodyBytes != null)
        {
            httpContext.Request.Body = new MemoryStream(bodyBytes);
            httpContext.Request.ContentLength = bodyBytes.Length;
            httpContext.Request.ContentType = "application/json";
        }

        if (routeValues != null)
        {
            httpContext.Request.RouteValues = routeValues;
        }

        context.HttpContext = httpContext;
    }

    /// <summary>
    /// Provides an empty service provider so population benchmarks do not include dependency injection cost.
    /// </summary>
    private sealed class PopulationEmptyServiceProvider : IServiceProvider
    {
        /// <inheritdoc />
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}

// ---------------------------------------------------------------------------
// Benchmark operations — each exercises a different binding source profile.
// These are discovered by the existing ValidationBenchmarksApp assembly scan.
// ---------------------------------------------------------------------------

/// <summary>
/// GET with one string route parameter, no body — baseline for route extraction cost.
/// </summary>
[global::Blueprint.Model.Attributes.RootLink("/bench-pop/route-only/{Id}")]
public partial class RouteOnlyGetBenchmarkQuery : IQuery<string>
{
    /// <summary>Gets or sets the route-bound identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Returns the bound identifier so the benchmark exercises the full pipeline path.</summary>
    /// <param name="operation">The populated operation instance.</param>
    /// <returns>A completed task containing a concatenated value representing all bound properties.</returns>
    public static Task<string> HandleAsync(RouteOnlyGetBenchmarkQuery operation)
    {
        return Task.FromResult(operation.Id);
    }
}

/// <summary>
/// POST with three string body properties, no route parameter — pure body parsing cost.
/// </summary>
[global::Blueprint.Model.Attributes.RootLink("/bench-pop/body-only-3")]
public partial class BodyOnly3StringsBenchmarkCommand : ICommand<string>
{
    /// <summary>Gets or sets a body-bound name value.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets a body-bound email value.</summary>
    public string? Email { get; set; }

    /// <summary>Gets or sets a body-bound department value.</summary>
    public string? Department { get; set; }

    /// <summary>Returns concatenated property values so the benchmark exercises the full pipeline path.</summary>
    /// <param name="operation">The populated operation instance.</param>
    /// <returns>A completed task containing a concatenated value representing all bound properties.</returns>
    public static Task<string> HandleAsync(BodyOnly3StringsBenchmarkCommand operation)
    {
        return Task.FromResult($"{operation.Name}:{operation.Email}:{operation.Department}");
    }
}

/// <summary>
/// POST with one route parameter and two body properties — most common real-world profile.
/// </summary>
[global::Blueprint.Model.Attributes.RootLink("/bench-pop/mixed/{Id}")]
public partial class MixedRouteBodyBenchmarkCommand : ICommand<string>
{
    /// <summary>Gets or sets the route-bound identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets a body-bound name value.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets a body-bound email value.</summary>
    public string? Email { get; set; }

    /// <summary>Returns concatenated property values so the benchmark exercises the full pipeline path.</summary>
    /// <param name="operation">The populated operation instance.</param>
    /// <returns>A completed task containing a concatenated value representing all bound properties.</returns>
    public static Task<string> HandleAsync(MixedRouteBodyBenchmarkCommand operation)
    {
        return Task.FromResult($"{operation.Id}:{operation.Name}:{operation.Email}");
    }
}

/// <summary>
/// POST with typed body properties (int, bool, string, Guid) plus a string route — type conversion cost.
/// </summary>
[global::Blueprint.Model.Attributes.RootLink("/bench-pop/typed/{Id}")]
public partial class TypedBodyBenchmarkCommand : ICommand<string>
{
    /// <summary>Gets or sets the route-bound identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets an integer body-bound value.</summary>
    public int Count { get; set; }

    /// <summary>Gets or sets a boolean body-bound value.</summary>
    public bool Active { get; set; }

    /// <summary>Gets or sets a string body-bound label.</summary>
    public string? Label { get; set; }

    /// <summary>Gets or sets a Guid body-bound tag.</summary>
    public Guid Tag { get; set; }

    /// <summary>Returns concatenated property values so the benchmark exercises the full pipeline path.</summary>
    /// <param name="operation">The populated operation instance.</param>
    /// <returns>A completed task containing a concatenated value representing all bound properties.</returns>
    public static Task<string> HandleAsync(TypedBodyBenchmarkCommand operation)
    {
        return Task.FromResult($"{operation.Id}:{operation.Count}:{operation.Active}:{operation.Label}:{operation.Tag}");
    }
}

/// <summary>
/// POST with 10 string body properties — scaling behaviour for larger operations.
/// </summary>
[global::Blueprint.Model.Attributes.RootLink("/bench-pop/large-body")]
public partial class LargeBodyBenchmarkCommand : ICommand<string>
{
    /// <summary>Gets or sets body field 1.</summary>
    public string? Field1 { get; set; }

    /// <summary>Gets or sets body field 2.</summary>
    public string? Field2 { get; set; }

    /// <summary>Gets or sets body field 3.</summary>
    public string? Field3 { get; set; }

    /// <summary>Gets or sets body field 4.</summary>
    public string? Field4 { get; set; }

    /// <summary>Gets or sets body field 5.</summary>
    public string? Field5 { get; set; }

    /// <summary>Gets or sets body field 6.</summary>
    public string? Field6 { get; set; }

    /// <summary>Gets or sets body field 7.</summary>
    public string? Field7 { get; set; }

    /// <summary>Gets or sets body field 8.</summary>
    public string? Field8 { get; set; }

    /// <summary>Gets or sets body field 9.</summary>
    public string? Field9 { get; set; }

    /// <summary>Gets or sets body field 10.</summary>
    public string? Field10 { get; set; }

    /// <summary>Returns concatenated property values so the benchmark exercises the full pipeline path.</summary>
    /// <param name="operation">The populated operation instance.</param>
    /// <returns>A completed task containing a concatenated value representing all bound properties.</returns>
    public static Task<string> HandleAsync(LargeBodyBenchmarkCommand operation)
    {
        return Task.FromResult(
            $"{operation.Field1}:{operation.Field2}:{operation.Field3}:{operation.Field4}:{operation.Field5}:" +
            $"{operation.Field6}:{operation.Field7}:{operation.Field8}:{operation.Field9}:{operation.Field10}");
    }
}

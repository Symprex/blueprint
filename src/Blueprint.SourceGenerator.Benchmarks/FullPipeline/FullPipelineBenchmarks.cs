using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Benchmarks.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Benchmarks.FullPipeline;

/// <summary>
/// Measures end-to-end pipeline execution cost across representative real-world scenarios.
/// Each benchmark exercises the full generated pipeline path: validation, handler invocation,
/// and result wrapping — mirroring production S365 patterns at various complexity levels.
/// </summary>
[Config(typeof(InProcessBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class FullPipelineBenchmarks
{
    private readonly ValidationBenchmarksApp _app = new();
    private readonly ValidationBenchmarksAppExecutor _executor;
    private readonly System.IServiceProvider _serviceProvider;
    private readonly ApiOperationDescriptor _minimalDescriptor;
    private readonly ApiOperationDescriptor _validatedDescriptor;
    private readonly ApiOperationDescriptor _fullStackDescriptor;

    /// <summary>
    /// Initializes the benchmark fixture with a service provider and pre-warmed executor.
    /// Descriptors are cached to avoid per-iteration allocation (mirrors production where
    /// descriptors are created once at app startup).
    /// </summary>
    public FullPipelineBenchmarks()
    {
        this._serviceProvider = new ServiceCollection().BuildServiceProvider();

        this._executor = new ValidationBenchmarksAppExecutor(
            NullLoggerFactory.Instance,
            this._app,
            this._serviceProvider);

        this._minimalDescriptor = FpMinimalBenchmarkQuery.Configure();
        this._validatedDescriptor = FpValidatedBenchmarkQuery.Configure();
        this._fullStackDescriptor = FpFullStackBenchmarkCommand.Configure();
    }

    /// <summary>
    /// Baseline: minimal query with no validation — just handler invocation.
    /// Represents the absolute minimum pipeline overhead.
    /// </summary>
    /// <returns>The operation result from the minimal pipeline.</returns>
    [Benchmark(Baseline = true)]
    public ValueTask<OperationResult> MinimalQueryAsync()
    {
        var context = new ApiOperationContext(
            this._serviceProvider,
            this._app,
            this._minimalDescriptor,
            CancellationToken.None)
        {
            HttpContext = new DefaultHttpContext(),
        };

        return this._executor.GetPipeline(typeof(FpMinimalBenchmarkQuery)).ExecuteAsync(context);
    }

    /// <summary>
    /// Query with [Required] and [MaxLength] validation on two properties — valid input.
    /// Measures fast-path validation cost on top of handler invocation.
    /// </summary>
    /// <returns>The operation result after successful validation.</returns>
    [Benchmark]
    public ValueTask<OperationResult> ValidatedQueryValidInputAsync()
    {
        var context = new ApiOperationContext(
            this._serviceProvider,
            this._app,
            this._validatedDescriptor,
            CancellationToken.None)
        {
            HttpContext = new DefaultHttpContext(),
            Operation = new FpValidatedBenchmarkQuery { Name = "Alice", Email = "alice@example.com" },
        };

        return this._executor.GetPipeline(typeof(FpValidatedBenchmarkQuery)).ExecuteAsync(context);
    }

    /// <summary>
    /// Query with [Required] validation — invalid (null) input that short-circuits.
    /// Measures validation failure cost and pipeline short-circuit overhead.
    /// </summary>
    /// <returns>The validation failure result.</returns>
    [Benchmark]
    public ValueTask<OperationResult> ValidatedQueryInvalidInputAsync()
    {
        var context = new ApiOperationContext(
            this._serviceProvider,
            this._app,
            this._validatedDescriptor,
            CancellationToken.None)
        {
            HttpContext = new DefaultHttpContext(),
            Operation = new FpValidatedBenchmarkQuery { Name = null!, Email = null! },
        };

        return this._executor.GetPipeline(typeof(FpValidatedBenchmarkQuery)).ExecuteAsync(context);
    }

    /// <summary>
    /// Full-stack command with multi-property validation — valid input.
    /// Exercises validation + handler invocation on a command with three validated properties.
    /// </summary>
    /// <returns>The operation result from the full-stack pipeline.</returns>
    [Benchmark]
    public ValueTask<OperationResult> FullStackCommandValidAsync()
    {
        var context = new ApiOperationContext(
            this._serviceProvider,
            this._app,
            this._fullStackDescriptor,
            CancellationToken.None)
        {
            HttpContext = new DefaultHttpContext(),
            Operation = new FpFullStackBenchmarkCommand
            {
                TenantId = "tenant-42",
                Name = "test-operation",
                Description = "A benchmark test description that exercises the typical payload size",
            },
        };

        return this._executor.GetPipeline(typeof(FpFullStackBenchmarkCommand)).ExecuteAsync(context);
    }

    /// <summary>
    /// Full-stack command with validation failure — measures the cost when validation
    /// short-circuits before handler invocation.
    /// </summary>
    /// <returns>The validation failure result from the full-stack pipeline.</returns>
    [Benchmark]
    public ValueTask<OperationResult> FullStackCommandValidationFailureAsync()
    {
        var context = new ApiOperationContext(
            this._serviceProvider,
            this._app,
            this._fullStackDescriptor,
            CancellationToken.None)
        {
            HttpContext = new DefaultHttpContext(),
            Operation = new FpFullStackBenchmarkCommand
            {
                TenantId = null!,
                Name = null!,
                Description = null,
            },
        };

        return this._executor.GetPipeline(typeof(FpFullStackBenchmarkCommand)).ExecuteAsync(context);
    }
}

// ─── Benchmark Operations ────────────────────────────────────────────
// Prefixed with "Fp" to avoid name collisions with operations in other benchmark files.

/// <summary>
/// Minimal query with no validation — the cheapest possible pipeline.
/// </summary>
[RootLink("/benchmarks/fp/minimal")]
[AllowAnonymous]
public partial class FpMinimalBenchmarkQuery : IQuery<string>
{
    /// <summary>
    /// Handles this minimal query benchmark by returning a static result.
    /// </summary>
    /// <param name="context">The operation execution context.</param>
    /// <returns>A static "ok" string result.</returns>
    public Task<string> HandleAsync(ApiOperationContext context)
    {
        return Task.FromResult("ok");
    }
}

/// <summary>
/// Query with standard validation attributes — exercises the compiled validation fast-path.
/// </summary>
[RootLink("/benchmarks/fp/validated")]
[AllowAnonymous]
public partial class FpValidatedBenchmarkQuery : IQuery<string>
{
    /// <summary>
    /// Gets or sets the required name property.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    /// <summary>
    /// Gets or sets the required email property.
    /// </summary>
    [Required]
    [EmailAddress]
    public string Email { get; set; } = null!;

    /// <summary>
    /// Handles this validated query benchmark by returning a static result.
    /// </summary>
    /// <param name="context">The operation execution context.</param>
    /// <returns>A static "ok" string result.</returns>
    public Task<string> HandleAsync(ApiOperationContext context)
    {
        return Task.FromResult("ok");
    }
}

/// <summary>
/// Full-stack command with multi-property validation — the most representative
/// production pipeline shape combining validation with handler invocation.
/// </summary>
[RootLink("/benchmarks/fp/fullstack")]
[AllowAnonymous]
public partial class FpFullStackBenchmarkCommand : ICommand<string>
{
    /// <summary>
    /// Gets or sets the required tenant ID.
    /// </summary>
    [Required]
    public string TenantId { get; set; } = null!;

    /// <summary>
    /// Gets or sets the required name.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = null!;

    /// <summary>
    /// Gets or sets an optional description.
    /// </summary>
    [MaxLength(2000)]
    public string? Description { get; set; }

    /// <summary>
    /// Handles this full-stack command benchmark by returning the tenant ID.
    /// </summary>
    /// <param name="context">The operation execution context.</param>
    /// <returns>The tenant ID string.</returns>
    public Task<string> HandleAsync(ApiOperationContext context)
    {
        return Task.FromResult(this.TenantId);
    }
}

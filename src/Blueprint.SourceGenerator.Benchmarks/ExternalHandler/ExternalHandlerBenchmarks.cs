using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Benchmarks.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Benchmarks.ExternalHandler;

/// <summary>
/// Measures pipeline execution cost when using external handler classes — the dominant S365 pattern.
/// Compares inline handler overhead against external handler DI resolution and property injection to
/// quantify the cost of the S365 handler architecture.
/// </summary>
[Config(typeof(InProcessBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class ExternalHandlerBenchmarks
{
    private readonly ValidationBenchmarksApp _app = new();
    private readonly ValidationBenchmarksAppExecutor _executor;
    private readonly System.IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes the benchmark fixture with DI registrations that external handlers require.
    /// </summary>
    public ExternalHandlerBenchmarks()
    {
        var services = new ServiceCollection();
        services.AddTransient<ExtMinimalQueryHandler>();
        services.AddTransient<ExtValidatedCommandHandler>();
        services.AddTransient<ExtDiDependencyQueryHandler>();
        services.AddTransient<IBenchmarkRepository, InMemoryBenchmarkRepository>();
        this._serviceProvider = services.BuildServiceProvider();

        this._executor = new ValidationBenchmarksAppExecutor(
            NullLoggerFactory.Instance,
            this._app,
            this._serviceProvider);
    }

    /// <summary>
    /// Baseline: inline handler query (same as FullPipelineBenchmarks.MinimalQueryAsync).
    /// </summary>
    /// <returns>The result from the inline handler pipeline.</returns>
    [Benchmark(Baseline = true)]
    public Task<OperationResult> InlineHandlerQueryAsync()
    {
        var context = new ApiOperationContext(
            this._serviceProvider,
            this._app,
            ExtInlineBaselineQuery.Configure(),
            CancellationToken.None);

        return this._executor.GetPipeline(typeof(ExtInlineBaselineQuery)).ExecuteAsync(context);
    }

    /// <summary>
    /// External handler with no DI dependencies — measures the raw cost of handler class resolution and invocation
    /// compared to an inline handler.
    /// </summary>
    /// <returns>The result from the external handler pipeline.</returns>
    [Benchmark]
    public Task<OperationResult> ExternalHandlerMinimalQueryAsync()
    {
        var context = new ApiOperationContext(
            this._serviceProvider,
            this._app,
            ExtMinimalQuery.Configure(),
            CancellationToken.None);

        return this._executor.GetPipeline(typeof(ExtMinimalQuery)).ExecuteAsync(context);
    }

    /// <summary>
    /// External handler with validation on the operation — measures combined validation + external handler cost.
    /// </summary>
    /// <returns>The result from the validated external handler pipeline.</returns>
    [Benchmark]
    public Task<OperationResult> ExternalHandlerValidatedCommandAsync()
    {
        var context = new ApiOperationContext(
            this._serviceProvider,
            this._app,
            ExtValidatedCommand.Configure(),
            CancellationToken.None)
        {
            Operation = new ExtValidatedCommand
            {
                TenantId = "tenant-42",
                Name = "benchmark-item",
            },
        };

        return this._executor.GetPipeline(typeof(ExtValidatedCommand)).ExecuteAsync(context);
    }

    /// <summary>
    /// External handler with a constructor-injected DI dependency — measures the cost of resolving
    /// and injecting a service through the DI container at handler instantiation time.
    /// </summary>
    /// <returns>The result from the DI-dependent external handler pipeline.</returns>
    [Benchmark]
    public Task<OperationResult> ExternalHandlerWithDiDependencyAsync()
    {
        var context = new ApiOperationContext(
            this._serviceProvider,
            this._app,
            ExtDiDependencyQuery.Configure(),
            CancellationToken.None);

        return this._executor.GetPipeline(typeof(ExtDiDependencyQuery)).ExecuteAsync(context);
    }
}

// ─── Operations ──────────────────────────────────────────────────────
// Prefixed with "Ext" to avoid name collisions with other benchmark files.

/// <summary>
/// Inline handler baseline for comparing against external handler overhead.
/// </summary>
[RootLink("/benchmarks/ext/inline-baseline")]
[AllowAnonymous]
public partial class ExtInlineBaselineQuery : IQuery<string>
{
    /// <summary>
    /// Handles this inline baseline query by returning a static result.
    /// </summary>
    /// <param name="context">The operation execution context.</param>
    /// <returns>A static result string.</returns>
    public Task<string> HandleAsync(ApiOperationContext context)
    {
        return Task.FromResult("inline-ok");
    }
}

/// <summary>
/// Query handled by an external handler class with no DI dependencies.
/// </summary>
[RootLink("/benchmarks/ext/minimal")]
[AllowAnonymous]
public partial class ExtMinimalQuery : IQuery<string>;

/// <summary>
/// Command handled by an external handler class with validation attributes.
/// </summary>
[RootLink("/benchmarks/ext/validated")]
[AllowAnonymous]
public partial class ExtValidatedCommand : ICommand<string>
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
}

/// <summary>
/// Query handled by an external handler class with a constructor-injected dependency.
/// </summary>
[RootLink("/benchmarks/ext/with-di")]
[AllowAnonymous]
public partial class ExtDiDependencyQuery : IQuery<string>;

// ─── External Handlers ───────────────────────────────────────────────

/// <summary>
/// Minimal external handler with no dependencies — pure handler resolution overhead.
/// </summary>
public class ExtMinimalQueryHandler
{
    /// <summary>
    /// Handles the minimal external query by returning a static result.
    /// </summary>
    /// <param name="query">The query to handle.</param>
    /// <returns>A static result string.</returns>
    public Task<string> InvokeAsync(ExtMinimalQuery query)
    {
        return Task.FromResult("external-ok");
    }
}

/// <summary>
/// External handler for the validated command — mirrors S365's handler pattern.
/// </summary>
public class ExtValidatedCommandHandler
{
    /// <summary>
    /// Handles the validated command by returning the tenant ID.
    /// </summary>
    /// <param name="command">The command to handle.</param>
    /// <returns>The tenant ID from the command.</returns>
    public Task<string> InvokeAsync(ExtValidatedCommand command)
    {
        return Task.FromResult(command.TenantId);
    }
}

/// <summary>
/// External handler with a constructor-injected repository dependency — mirrors S365's typical handler shape.
/// </summary>
public class ExtDiDependencyQueryHandler
{
    private readonly IBenchmarkRepository _repository;

    /// <summary>
    /// Initializes the handler with the injected repository.
    /// </summary>
    /// <param name="repository">The benchmark repository dependency.</param>
    public ExtDiDependencyQueryHandler(IBenchmarkRepository repository)
    {
        this._repository = repository;
    }

    /// <summary>
    /// Handles the DI-dependent query by delegating to the repository.
    /// </summary>
    /// <param name="query">The query to handle.</param>
    /// <returns>A result from the repository.</returns>
    public Task<string> InvokeAsync(ExtDiDependencyQuery query)
    {
        return Task.FromResult(this._repository.GetValue());
    }
}

// ─── Benchmark DI Contracts ──────────────────────────────────────────

/// <summary>
/// Simple dependency interface for measuring DI resolution cost.
/// </summary>
public interface IBenchmarkRepository
{
    /// <summary>
    /// Gets a benchmark value to return from handlers.
    /// </summary>
    /// <returns>A static benchmark string.</returns>
    string GetValue();
}

/// <summary>
/// In-memory implementation of the benchmark repository for DI resolution benchmarks.
/// </summary>
public sealed class InMemoryBenchmarkRepository : IBenchmarkRepository
{
    /// <inheritdoc />
    public string GetValue()
    {
        return "di-ok";
    }
}

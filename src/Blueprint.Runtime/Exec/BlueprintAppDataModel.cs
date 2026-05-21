using System.Collections.Frozen;
using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint;

/// <summary>
/// Provides a runtime model for an exposed API, a mapping of descriptors to their associated pipelines.
/// After all registrations are complete, call <see cref="Freeze"/> to convert the internal lookup
/// to a <see cref="FrozenDictionary{TKey,TValue}"/> for optimal per-request dispatch performance.
/// </summary>
public class BlueprintAppExecutor
{
    private Dictionary<Type, IOperationExecutorPipeline>? _mutableOperations = new();
    private FrozenDictionary<Type, IOperationExecutorPipeline>? _frozenOperations;

    /// <summary>
    /// Initializes a new instance of the <see cref="BlueprintAppExecutor"/> class.
    /// </summary>
    /// <param name="app">The Blueprint app model whose operations this executor will dispatch.</param>
    public BlueprintAppExecutor(BlueprintApp app)
    {
        this.App = app;
    }

    /// <summary>
    /// Gets the Blueprint app metadata that owns the registered operation descriptors.
    /// </summary>
    public BlueprintApp App { get; }

    /// <summary>
    /// Registers the given operation descriptor. Must be called before <see cref="Freeze"/>.
    /// </summary>
    /// <param name="operationType">The type of the operation to register a pipeline for.</param>
    /// <param name="pipeline">The pipeline to register.</param>
    /// <exception cref="InvalidOperationException">Thrown if called after <see cref="Freeze"/>.</exception>
    public void Register(Type operationType, IOperationExecutorPipeline pipeline)
    {
        if (this._mutableOperations is null)
        {
            throw new InvalidOperationException(
                "Cannot register pipelines after the executor has been frozen. " +
                "Ensure all Register calls occur before Freeze().");
        }

        this._mutableOperations[operationType] = pipeline;
    }

    /// <summary>
    /// Freezes the internal lookup dictionary into a <see cref="FrozenDictionary{TKey,TValue}"/>
    /// for optimal read performance on every request dispatch. Generated executors call this
    /// at the end of their constructor after all pipelines have been registered.
    /// </summary>
    public void Freeze()
    {
        if (this._mutableOperations is null)
        {
            return;
        }

        this._frozenOperations = this._mutableOperations.ToFrozenDictionary();
        this._mutableOperations = null;
    }

    /// <summary>
    /// Gets the pipeline for the given operation descriptor.
    /// </summary>
    /// <param name="operationType">The type of the operation to get the pipeline for.</param>
    /// <returns>The pipeline for the given operation descriptor.</returns>
    /// <exception cref="InvalidOperationException">If no operation of the specified type has been registered.</exception>
    public IOperationExecutorPipeline GetPipeline(Type operationType)
    {
        // Fast path: frozen dictionary (normal runtime)
        if (this._frozenOperations is not null)
        {
            return this._frozenOperations.TryGetValue(operationType, out var pipeline)
                ? pipeline
                : throw new InvalidOperationException($"Cannot find a registered operation with the name '{operationType}'.");
        }

        // Fallback: mutable dictionary (before Freeze() is called)
        return this._mutableOperations!.TryGetValue(operationType, out var mutablePipeline)
            ? mutablePipeline
            : throw new InvalidOperationException($"Cannot find a registered operation with the name '{operationType}'.");
    }

    /// <summary>
    /// Resolves and executes the registered pipeline for the operation described by the supplied context.
    /// </summary>
    /// <param name="context">The execution context whose descriptor identifies the operation to dispatch.</param>
    /// <returns>The result produced by the resolved pipeline.</returns>
    public ValueTask<OperationResult> ExecuteAsync(ApiOperationContext context)
    {
        return this.GetPipeline(context.Descriptor.OperationType).ExecuteAsync(context);
    }

    /// <summary>
    /// Creates a new DI scope and executes the given operation through the generated pipeline.
    /// This is the equivalent of legacy Blueprint's <c>IApiOperationExecutor.ExecuteWithNewScopeAsync</c>
    /// and is used for programmatic operation dispatch from CLI commands, nested handler calls, or tests.
    /// </summary>
    /// <param name="operation">The operation instance to execute.</param>
    /// <param name="serviceProvider">
    /// The service provider to create a scope from. Typically the root provider or a parent scope's provider.
    /// </param>
    /// <param name="cancellationToken">Cancellation token to abort execution.</param>
    /// <returns>The result produced by the resolved pipeline.</returns>
    /// <exception cref="InvalidOperationException">If no descriptor is registered for the operation type.</exception>
    public async Task<OperationResult> ExecuteWithNewScopeAsync(
        IOperation operation,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var operationType = operation.GetType();
        var descriptor = this.App.FindOperation(operationType)
            ?? throw new InvalidOperationException(
                $"No operation descriptor found for type '{operationType.FullName}'. " +
                $"Ensure the operation is registered in the BlueprintApp.");

        using var scope = serviceProvider.CreateScope();

        var context = new ApiOperationContext(
            scope.ServiceProvider,
            this.App,
            descriptor,
            cancellationToken)
        {
            Operation = operation,
        };

        return await this.ExecuteAsync(context).ConfigureAwait(false);
    }
}

using Blueprint.Model;

namespace Blueprint;

/// <summary>
/// Provides a runtime model for an exposed API, a mapping of descriptors to their associated pipelines..
/// </summary>
public class BlueprintAppExecutor
{
    private readonly Dictionary<Type, IOperationExecutorPipeline> _allOperations = new();

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
    /// Registers the given operation descriptor.
    /// </summary>
    /// <param name="operationType">The type of the operation to register a pipeline for.</param>
    /// <param name="pipeline">The pipeline to register.</param>
    public void Register(Type operationType, IOperationExecutorPipeline pipeline)
    {
        this._allOperations[operationType] = pipeline;
    }

    /// <summary>
    /// Gets the pipeline for the given operation descriptor.
    /// </summary>
    /// <param name="operationType">The type of the operation to get the pipeline for.</param>
    /// <returns>The pipeline for the given operation descriptor.</returns>
    /// <exception cref="InvalidOperationException">If no operation of the specified type has been registered.</exception>
    public IOperationExecutorPipeline GetPipeline(Type operationType)
    {
        return !this._allOperations.TryGetValue(operationType, out var pipeline)
            ? throw new InvalidOperationException($"Cannot find a registered operation with the name '{operationType}'.")
            : pipeline;
    }
}

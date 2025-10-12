using Blueprint.Model;

namespace Blueprint;

/// <summary>
/// Provides a runtime model for an exposed API, a mapping of descriptors to their associated pipelines..
/// </summary>
public class BlueprintRuntimeModel
{
    private readonly Dictionary<Type, IOperationExecutorPipeline> _allOperations = new();

    public BlueprintRuntimeModel(BlueprintAppDataModel appDataModel)
    {
        this.AppDataModel = appDataModel;
    }

    public BlueprintAppDataModel AppDataModel { get; }

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
        if (!this._allOperations.TryGetValue(operationType, out var pipeline))
        {
            throw new InvalidOperationException($"Cannot find a registered operation with the name '{operationType}'.");
        }

        return pipeline;
    }
}

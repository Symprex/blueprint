namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// Ordered pipeline stages shared across generated execution plans.
/// </summary>
public enum PipelineStage
{
    /// <summary>
    /// Sets up locals and context state needed by later pipeline stages.
    /// </summary>
    Setup,

    /// <summary>
    /// Populates derived data before validation and execution.
    /// </summary>
    Population,

    /// <summary>
    /// Resolves identity and authentication state.
    /// </summary>
    Authentication,

    /// <summary>
    /// Applies authorisation checks once identity data is available.
    /// </summary>
    Authorisation,

    /// <summary>
    /// Runs generated or runtime validation before handler execution.
    /// </summary>
    Validation,

    /// <summary>
    /// Invokes the operation handler or equivalent execution step.
    /// </summary>
    Execution,

    /// <summary>
    /// Runs result-shaping or follow-up steps after handler execution.
    /// </summary>
    PostExecution,

    /// <summary>
    /// Performs final cleanup before the pipeline returns.
    /// </summary>
    Cleanup,
}

namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// Ordered pipeline stages shared across generated execution plans.
/// </summary>
public enum PipelineStage
{
    Setup,
    Population,
    Authentication,
    Authorisation,
    Validation,
    Execution,
    PostExecution,
    Cleanup,
}

namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// Context passed to pipeline contributors.
/// </summary>
/// <param name="Operation">The current operation.</param>
/// <param name="Plan">The pipeline plan under construction.</param>
/// <param name="OperationLookup">A lookup for related operations.</param>
public sealed record PipelineContributorContext(
    OperationModel Operation,
    PipelinePlan Plan,
    IOperationLookup OperationLookup);

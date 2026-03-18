namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// Context passed to descriptor contributors.
/// </summary>
/// <param name="Operation">The current operation.</param>
/// <param name="Plan">The descriptor plan under construction.</param>
/// <param name="OperationLookup">A lookup for related operations.</param>
public sealed record DescriptorContributorContext(
    OperationModel Operation,
    DescriptorPlan Plan,
    IOperationLookup OperationLookup);

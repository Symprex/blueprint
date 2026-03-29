using System.Collections.Immutable;

namespace Blueprint.Model;

/// <summary>
/// Feature data populated during descriptor generation with the flattened semantic outcomes declared by an operation contract.
/// </summary>
/// <param name="Outcomes">The ordered flattened semantic outcomes for the operation.</param>
public sealed record OperationResultOutcomesFeatureData(ImmutableArray<OperationResultOutcome> Outcomes);

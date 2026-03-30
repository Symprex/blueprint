using System.Collections.Immutable;

namespace Blueprint.Model;

/// <summary>
/// Feature data populated during descriptor generation with the ordered failure outcomes exposed by the approved
/// exception translator policy.
/// </summary>
/// <param name="Outcomes">The ordered failure outcomes for the operation.</param>
public sealed record OperationFailureOutcomesFeatureData(ImmutableArray<OperationFailureOutcome> Outcomes);

using System.Collections.Immutable;

namespace Blueprint.Model;

/// <summary>
/// Feature data populated during descriptor generation with the currently known host surfaces that expose an operation.
/// </summary>
/// <param name="HostNames">The stable host names that expose the operation.</param>
public sealed record OperationHostExposureFeatureData(ImmutableArray<string> HostNames);

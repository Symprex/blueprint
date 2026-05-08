namespace Blueprint.Model;

/// <summary>
/// Feature data populated during descriptor generation with the stable operation group name used by downstream OpenAPI
/// and inspection surfaces.
/// </summary>
/// <param name="GroupName">The deterministic group name for the operation when one is available.</param>
/// <param name="GroupDescription">An optional human-readable description for the operation group, published as the OpenAPI tag description.</param>
public sealed record OperationGroupMetadataFeatureData(string GroupName, string? GroupDescription = null);

using System.Collections.Immutable;

namespace Blueprint.Model;

/// <summary>
/// Feature data populated during descriptor generation with the body-bearing resource-event outcomes that expose
/// compile-time-stable event identifiers for later discrimination and OpenAPI rendering.
/// </summary>
/// <param name="Outcomes">The distinct body-bearing resource-event outcomes proven from the declared semantic contract.</param>
public sealed record OperationResourceEventMetadataFeatureData(ImmutableArray<OperationResourceEventMetadata> Outcomes);

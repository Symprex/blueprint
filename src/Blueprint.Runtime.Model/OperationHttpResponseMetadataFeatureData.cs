using System.Collections.Immutable;

namespace Blueprint.Model;

/// <summary>
/// Feature data populated during descriptor generation with the HTTP response shapes currently derivable from descriptor
/// outcomes and host-aware failure metadata.
/// </summary>
/// <param name="Responses">The distinct HTTP responses projected for the operation.</param>
public sealed record OperationHttpResponseMetadataFeatureData(ImmutableArray<OperationHttpResponseMetadata> Responses);

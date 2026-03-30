using System.Collections.Immutable;

namespace Blueprint.Model;

/// <summary>
/// Describes one HTTP response shape projected from descriptor-level semantic result and failure-outcome metadata.
/// </summary>
/// <param name="StatusCode">The HTTP status code emitted for this response family.</param>
/// <param name="HasResponseBody">Whether the current response family writes a response body.</param>
/// <param name="ResultTypeNames">The semantic result type names that collapse into this HTTP response.</param>
public sealed record OperationHttpResponseMetadata(
    int StatusCode,
    bool HasResponseBody,
    ImmutableArray<string> ResultTypeNames);

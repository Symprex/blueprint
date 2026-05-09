using System.Collections.Immutable;

namespace Blueprint.Model;

/// <summary>
/// Feature data populated during descriptor generation with XML documentation extracted at compile time.
/// Provides summary and remarks text for operations and per-property summaries for use by OpenAPI
/// rendering and inspection surfaces, avoiding runtime XML file loading or reflection-based extraction.
/// </summary>
/// <param name="Summary">The XML documentation summary for the operation type, or <c>null</c> when absent.</param>
/// <param name="Remarks">The XML documentation remarks for the operation type, or <c>null</c> when absent.</param>
/// <param name="PropertySummaries">Per-property XML documentation summaries keyed by property name.</param>
public sealed record OperationDocumentationFeatureData(
    string? Summary,
    string? Remarks,
    ImmutableDictionary<string, string> PropertySummaries);

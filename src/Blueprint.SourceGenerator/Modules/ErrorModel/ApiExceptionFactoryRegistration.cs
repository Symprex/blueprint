namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Carries compile-time metadata for a single <c>ApiExceptionFactory</c> field discovered on an operation type,
/// so <see cref="HttpResponseMetadataModule"/> can project an additional error response into OpenAPI documentation.
/// </summary>
/// <param name="FieldName">The name of the static field for deterministic ordering and diagnostics.</param>
/// <param name="HttpStatus">The HTTP status code extracted from the factory constructor.</param>
internal sealed record ApiExceptionFactoryRegistration(
    string FieldName,
    int HttpStatus);

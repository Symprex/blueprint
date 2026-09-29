namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Carries compile-time metadata for a single <c>ApiExceptionFactory</c> field discovered on an operation type,
/// so <see cref="HttpResponseMetadataModule"/> can project an additional error response into OpenAPI documentation.
/// </summary>
/// <param name="FieldName">The name of the static field for deterministic ordering and diagnostics.</param>
/// <param name="HttpStatus">The HTTP status code extracted from the factory constructor.</param>
/// <param name="FieldReference">
/// The fully qualified expression naming the field (<c>global::Ns.Operation.Field</c>), so generated code can read the
/// factory's title and type from the field itself, as legacy's convention read the field's value.
/// </param>
internal sealed record ApiExceptionFactoryRegistration(
    string FieldName,
    int HttpStatus,
    string FieldReference);

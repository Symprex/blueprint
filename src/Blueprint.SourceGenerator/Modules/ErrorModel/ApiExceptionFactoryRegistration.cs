namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Carries compile-time metadata for a single <c>ApiExceptionFactory</c> field discovered on an operation type, or on the
/// inner operation a gateway operation exposes, so <see cref="HttpResponseMetadataModule"/> can project an additional error
/// response into OpenAPI documentation.
/// </summary>
/// <param name="FieldName">The name of the static field for deterministic ordering and diagnostics.</param>
/// <param name="HttpStatus">The HTTP status code extracted from the factory constructor.</param>
/// <param name="ProblemExample">
/// The C# construction of the <c>OperationProblemExample</c> documenting the factory: reading the title and type from the
/// field itself (<c>global::Ns.Operation.Field.Title</c>), as legacy's convention read the field's value, or, for an inner
/// operation's factory, from the constants its initialiser or its assembly's index records.
/// </param>
internal sealed record ApiExceptionFactoryRegistration(
    string FieldName,
    int HttpStatus,
    string ProblemExample);

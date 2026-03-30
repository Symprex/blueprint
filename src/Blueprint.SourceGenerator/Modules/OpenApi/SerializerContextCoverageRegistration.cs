namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Carries descriptor-planning metadata for one distinct body payload type that should later be validated against an
/// explicit serializer-context seam. This stays generator-local so serializer coverage can be planned without adding a
/// compile-time dependency on runtime-model feature-data types.
/// </summary>
/// <param name="BodyTypeName">The distinct body payload CLR type name requiring serializer coverage.</param>
internal sealed record SerializerContextCoverageRegistration(string BodyTypeName);

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Carries generator-local planning metadata for one resolved HTTP method so descriptor equality remains deterministic
/// without taking a compile-time dependency on runtime-model feature-data types.
/// </summary>
/// <param name="HttpMethod">The resolved uppercase HTTP method for the operation.</param>
internal sealed record OperationHttpMethodMetadataRegistration(string HttpMethod);

using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Diagnostics emitted by <see cref="ApiExceptionFactoryModule"/> when compile-time extraction
/// of <c>ApiExceptionFactory</c> field metadata encounters unsupported initializer patterns.
/// </summary>
internal static class ApiExceptionFactoryDiagnosticDescriptors
{
    private const string Category = "BlueprintApiExceptionFactory";

    /// <summary>
    /// Emitted when a <c>public static readonly ApiExceptionFactory</c> field on an operation type has an initializer
    /// whose HTTP status code cannot be resolved at compile time. The field will not contribute an OpenAPI error response.
    /// </summary>
    public static readonly DiagnosticDescriptor NonEvaluableFactory = new(
        id: "BP010",
        title: "Non-evaluable ApiExceptionFactory initializer",
        messageFormat: "The ApiExceptionFactory field '{0}' on operation '{1}' has an initializer whose HTTP status code could not be resolved at compile time; the field will not appear in OpenAPI error responses",
        category: Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "ApiExceptionFactory fields on operation types must use a literal int or System.Net.HttpStatusCode enum member as the third constructor argument so the source generator can project them into OpenAPI documentation at compile time.");
}

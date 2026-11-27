using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Diagnostics for an operation's unit of work persistence, reported by <see cref="UnitOfWorkModule"/>.
/// </summary>
internal static class PersistenceDiagnosticDescriptors
{
    private const string Category = "Blueprint.Persistence";

    /// <summary>
    /// BPP002: a <c>[SaveRefusedError]</c> names no static <c>ApiExceptionFactory</c> of the operation it is applied to, so
    /// the pipeline has no factory to answer a refused save with.
    /// </summary>
    public static readonly DiagnosticDescriptor SaveRefusedErrorIsNotAFactory = new(
        id: "BPP002",
        title: "[SaveRefusedError] must name a static ApiExceptionFactory",
        messageFormat: "[SaveRefusedError(\"{0}\")] on '{1}' names no static ApiExceptionFactory field or property of '{1}' called '{0}'",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[SaveRefusedError] names, usually with nameof, a static ApiExceptionFactory field or property declared on the operation class itself. The generated pipeline answers a refused unit of work save with that factory's problem.");
}

using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Diagnostics for an operation's unit of work persistence, reported by <see cref="UnitOfWorkModule"/>.
/// </summary>
internal static class PersistenceDiagnosticDescriptors
{
    private const string Category = "Blueprint.Persistence";

    /// <summary>
    /// BPP002: a <c>[ConcurrencyConflictError]</c> names no static <c>ApiExceptionFactory</c> of the operation it is applied to, so
    /// the pipeline has no factory to answer a concurrency conflict with.
    /// </summary>
    public static readonly DiagnosticDescriptor ConcurrencyConflictErrorIsNotAFactory = new(
        id: "BPP002",
        title: "[ConcurrencyConflictError] must name a static ApiExceptionFactory",
        messageFormat: "[ConcurrencyConflictError(\"{0}\")] on '{1}' names no static ApiExceptionFactory field or property of '{1}' called '{0}' that the generated pipeline can read",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[ConcurrencyConflictError] names, usually with nameof, a static ApiExceptionFactory field or property declared on the operation class itself. The generated pipeline, a class of its own, reads it to answer a unit of work save that meets a concurrency conflict, so it cannot be private or have a private getter, and must be public when the operation comes from a referenced assembly.");
}

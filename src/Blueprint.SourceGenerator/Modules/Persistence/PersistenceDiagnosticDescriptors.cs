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

    /// <summary>
    /// BPP003: <c>[RetryOnConflict]</c> on an operation with <c>[DoNotSaveChanges]</c>, whose pipeline has no unit of work
    /// save to be refused.
    /// </summary>
    public static readonly DiagnosticDescriptor RetryOnConflictWithoutSave = new(
        id: "BPP003",
        title: "[RetryOnConflict] needs a unit of work save",
        messageFormat: "[RetryOnConflict] on '{0}' has nothing to rerun: the operation has [DoNotSaveChanges], so its pipeline makes no unit of work save to be refused",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[RetryOnConflict] reruns an operation whose unit of work save, made by the generated pipeline after the handler, is refused. An operation with [DoNotSaveChanges] saves for itself, and its own save's refusal is not rerun. Remove one of the two attributes.");

    /// <summary>
    /// BPP004: <c>[RetryOnConflict]</c> on an operation with <c>[NoRebusTransaction]</c>, whose sends join the host's
    /// transaction and so survive a refused attempt.
    /// </summary>
    public static readonly DiagnosticDescriptor RetryOnConflictWithoutRebusTransaction = new(
        id: "BPP004",
        title: "[RetryOnConflict] needs the pipeline's Rebus transaction",
        messageFormat: "[RetryOnConflict] on '{0}' would send a refused attempt's messages: with [NoRebusTransaction] they join the host's transaction, which the rerun does not discard",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A rerun is safe only if what the refused attempt sent is discarded with it. The pipeline's own Rebus transaction is disposed uncompleted on a refusal, dropping its messages. With [NoRebusTransaction] there is none, and a message sent through IBus goes out with the host's transaction (a received message's) or at once. Remove one of the two attributes.");

    /// <summary>
    /// BPP005: <c>[RetryOnConflict]</c> asks for a number of reruns outside 1 to <c>RetryOnConflictAttribute.MaxRetries</c>.
    /// </summary>
    public static readonly DiagnosticDescriptor RetryOnConflictCountOutOfRange = new(
        id: "BPP005",
        title: "[RetryOnConflict] must rerun 1 to 5 times",
        messageFormat: "[RetryOnConflict({0})] on '{1}' must ask for 1 to 5 reruns",
        category: Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Each rerun runs the whole pipeline again in a fresh scope. Fewer than one does nothing; a save refused more than five times in a row is contention that rerunning will not fix, and the caller is better answered with the 409.");
}

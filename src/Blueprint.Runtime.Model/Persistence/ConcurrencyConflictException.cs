using JetBrains.Annotations;

namespace Blueprint.Persistence;

/// <summary>
/// Thrown by <see cref="IUnitOfWork.SaveChangesAsync"/> when the save meets a concurrency conflict: another request wrote
/// first, so a document read in this request was changed since (optimistic concurrency), or a document inserted in it had
/// already been inserted by another (a unique claim lost to a race).
/// </summary>
/// <remarks>
/// <para>
/// The generated pipeline answers it with a 409 problem: the <c>ApiExceptionFactory</c> the operation names with
/// <see cref="ConcurrencyConflictErrorAttribute"/>, or Blueprint's own <c>concurrency-conflict</c> problem when it names
/// none. So a handler that expects a conflict only stores; it does not save part-way through, in a session of its own, to
/// catch the store's exception itself.
/// </para>
/// <para>
/// A unit of work raises it in place of its store's own exception, which is the <see cref="Exception.InnerException"/>,
/// so the cause is kept for logs and for code outside a pipeline, such as a plain message handler's commit, that sees it
/// escape. Its message is the problem's detail, so it says what the caller can do, not what the store saw. Any other
/// failure of the save is not one of these and escapes unchanged.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class ConcurrencyConflictException : Exception
{
    /// <summary>
    /// Initialises a new instance of the <see cref="ConcurrencyConflictException"/> class.
    /// </summary>
    /// <param name="message">The problem's detail: what the caller can do about the conflict.</param>
    /// <param name="innerException">The store's own exception.</param>
    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

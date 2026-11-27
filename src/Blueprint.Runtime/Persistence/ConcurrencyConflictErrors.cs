using System.Net;
using JetBrains.Annotations;

namespace Blueprint.Persistence;

/// <summary>
/// The problem a concurrency conflict at the unit of work's save (<see cref="ConcurrencyConflictException"/>) is answered
/// with when its operation names none of its own with <see cref="ConcurrencyConflictErrorAttribute"/>.
/// </summary>
[PublicAPI]
public static class ConcurrencyConflictErrors
{
    /// <summary>
    /// A 409 <c>concurrency-conflict</c> problem, titled "Concurrency conflict": another request changed or created the
    /// same data first, so the caller can refresh the resource and retry.
    /// </summary>
    public static readonly ApiExceptionFactory Default = new("Concurrency conflict", "concurrency-conflict", HttpStatusCode.Conflict);
}

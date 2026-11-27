using System.Net;
using JetBrains.Annotations;

namespace Blueprint.Persistence;

/// <summary>
/// The problem a refused unit of work save (<see cref="SaveRefusedException"/>) is answered with when its operation names
/// none of its own with <see cref="SaveRefusedErrorAttribute"/>.
/// </summary>
[PublicAPI]
public static class SaveRefusedErrors
{
    /// <summary>
    /// A 409 <c>save_refused</c> problem: another request changed or created the same data first, so the request may be
    /// retried against what is there now.
    /// </summary>
    public static readonly ApiExceptionFactory Default = new("Save refused", "save_refused", HttpStatusCode.Conflict);
}

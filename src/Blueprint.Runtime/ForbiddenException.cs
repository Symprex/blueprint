using Blueprint.Authorization;

namespace Blueprint;

/// <summary>
/// Thrown when the caller does not have permission to perform the requested operation. Translates to HTTP 403.
/// </summary>
public class ForbiddenException : ApiException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ForbiddenException"/> class.
    /// </summary>
    /// <param name="message">A description of why access was denied (used as the problem detail).</param>
    public ForbiddenException(string message)
        : base("Forbidden", "unauthorized", message, 403)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ForbiddenException"/> class from an
    /// <see cref="ExecutionAllowed"/> denial, preserving the failure reason and diagnostic metadata.
    /// </summary>
    /// <param name="failure">The authorization check result that caused the denial.</param>
    public ForbiddenException(ExecutionAllowed failure)
        : base(failure.Message, "unauthorized", failure.Reason, 403)
    {
        this.FailureReason = failure;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ForbiddenException"/> class.
    /// </summary>
    /// <param name="message">A description of why access was denied (used as the problem detail).</param>
    /// <param name="innerException">The inner exception.</param>
    public ForbiddenException(string message, Exception innerException)
        : base("Forbidden", "unauthorized", message, 403, innerException)
    {
    }

    /// <summary>
    /// Gets the authorization failure that caused this exception, if constructed from an
    /// <see cref="ExecutionAllowed"/> denial. <see langword="null"/> when constructed with
    /// a plain message string.
    /// </summary>
    public ExecutionAllowed? FailureReason { get; }
}

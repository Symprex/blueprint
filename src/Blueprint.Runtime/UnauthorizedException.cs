namespace Blueprint;

/// <summary>
/// Thrown when authentication is required but the user is anonymous or their credentials
/// are invalid. Translates to HTTP 401 with a well-known <c>security_failure</c> problem type.
/// </summary>
/// <remarks>
/// This differs from <see cref="ForbiddenException"/> (HTTP 403) which indicates the user
/// is authenticated but lacks the required permissions. Use <see cref="UnauthorizedException"/>
/// when no valid identity has been established.
/// </remarks>
public class UnauthorizedException : ApiException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnauthorizedException"/> class.
    /// </summary>
    /// <param name="message">A description of why authentication failed (used as the problem detail).</param>
    public UnauthorizedException(string message)
        : base("Unauthorized", "security_failure", message, 401)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UnauthorizedException"/> class.
    /// </summary>
    /// <param name="message">A description of why authentication failed (used as the problem detail).</param>
    /// <param name="innerException">The inner exception.</param>
    public UnauthorizedException(string message, Exception innerException)
        : base("Unauthorized", "security_failure", message, 401, innerException)
    {
    }
}

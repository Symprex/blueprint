namespace Blueprint;

/// <summary>
/// Thrown when authentication is required but the user is anonymous or their credentials
/// are invalid. Translates to HTTP 401 Unauthorized.
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
    /// <param name="message">A description of why authentication failed.</param>
    public UnauthorizedException(string message)
        : base(message, 401)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UnauthorizedException"/> class.
    /// </summary>
    /// <param name="message">A description of why authentication failed.</param>
    /// <param name="innerException">The inner exception.</param>
    public UnauthorizedException(string message, Exception innerException)
        : base(message, innerException, 401)
    {
    }
}

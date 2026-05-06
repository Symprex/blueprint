namespace Blueprint;

/// <summary>
/// Thrown when the caller does not have permission to perform the requested operation. Translates to HTTP 403.
/// </summary>
public class ForbiddenException : ApiException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ForbiddenException"/> class.
    /// </summary>
    /// <param name="message">A description of why access was denied.</param>
    public ForbiddenException(string message)
        : base(message, 403)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ForbiddenException"/> class.
    /// </summary>
    /// <param name="message">A description of why access was denied.</param>
    /// <param name="innerException">The inner exception.</param>
    public ForbiddenException(string message, Exception innerException)
        : base(message, innerException, 403)
    {
    }
}

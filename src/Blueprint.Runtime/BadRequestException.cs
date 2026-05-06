namespace Blueprint;

/// <summary>
/// Thrown when the request is malformed or contains invalid data. Translates to HTTP 400.
/// </summary>
public class BadRequestException : ApiException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BadRequestException"/> class.
    /// </summary>
    /// <param name="message">A description of what was invalid in the request.</param>
    public BadRequestException(string message)
        : base(message, 400)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BadRequestException"/> class.
    /// </summary>
    /// <param name="message">A description of what was invalid in the request.</param>
    /// <param name="innerException">The inner exception.</param>
    public BadRequestException(string message, Exception innerException)
        : base(message, innerException, 400)
    {
    }
}

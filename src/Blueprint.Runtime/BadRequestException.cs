namespace Blueprint;

/// <summary>
/// Thrown when the request is malformed or contains invalid data. Translates to HTTP 400
/// with a well-known <c>bad_request</c> problem type.
/// </summary>
public class BadRequestException : ApiException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BadRequestException"/> class.
    /// </summary>
    /// <param name="message">A description of what was invalid (used as the problem detail).</param>
    public BadRequestException(string message)
        : base("Bad Request", "bad_request", message, 400)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BadRequestException"/> class.
    /// </summary>
    /// <param name="message">A description of what was invalid (used as the problem detail).</param>
    /// <param name="innerException">The inner exception.</param>
    public BadRequestException(string message, Exception innerException)
        : base("Bad Request", "bad_request", message, 400, innerException)
    {
    }
}

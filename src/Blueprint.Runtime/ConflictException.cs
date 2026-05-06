namespace Blueprint;

/// <summary>
/// Thrown when an operation conflicts with the current state of a resource. Translates to HTTP 409.
/// </summary>
public class ConflictException : ApiException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictException"/> class.
    /// </summary>
    /// <param name="message">A description of the conflict.</param>
    public ConflictException(string message)
        : base(message, 409)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictException"/> class.
    /// </summary>
    /// <param name="message">A description of the conflict.</param>
    /// <param name="innerException">The inner exception.</param>
    public ConflictException(string message, Exception innerException)
        : base(message, innerException, 409)
    {
    }
}

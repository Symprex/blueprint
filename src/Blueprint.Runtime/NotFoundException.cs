namespace Blueprint;

/// <summary>
/// Thrown when a requested resource cannot be found. Translates to HTTP 404.
/// </summary>
public class NotFoundException : ApiException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NotFoundException"/> class.
    /// </summary>
    /// <param name="message">A description of what was not found.</param>
    public NotFoundException(string message)
        : base(message, 404)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NotFoundException"/> class.
    /// </summary>
    /// <param name="message">A description of what was not found.</param>
    /// <param name="innerException">The inner exception.</param>
    public NotFoundException(string message, Exception innerException)
        : base(message, innerException, 404)
    {
    }
}

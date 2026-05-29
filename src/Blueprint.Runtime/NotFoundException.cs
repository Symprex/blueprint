namespace Blueprint;

/// <summary>
/// Thrown when a requested resource cannot be found. Translates to HTTP 404 with
/// a well-known <c>not_found</c> problem type so consumers can distinguish this
/// from other 404 scenarios.
/// </summary>
public class NotFoundException : ApiException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NotFoundException"/> class.
    /// </summary>
    /// <param name="message">A description of what was not found (used as the problem detail).</param>
    public NotFoundException(string message)
        : base("The requested resource could not be found", "not_found", message, 404)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NotFoundException"/> class.
    /// </summary>
    /// <param name="message">A description of what was not found (used as the problem detail).</param>
    /// <param name="innerException">The inner exception.</param>
    public NotFoundException(string message, Exception innerException)
        : base("The requested resource could not be found", "not_found", message, 404, innerException)
    {
    }
}

namespace Blueprint;

/// <summary>
/// Base exception for API-related errors with HTTP status information.
/// </summary>
public class ApiException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApiException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="httpStatus">The HTTP status code.</param>
    public ApiException(string message, int httpStatus = 500)
        : base(message)
    {
        this.HttpStatus = httpStatus;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    /// <param name="httpStatus">The HTTP status code.</param>
    public ApiException(string message, Exception innerException, int httpStatus = 500)
        : base(message, innerException)
    {
        this.HttpStatus = httpStatus;
    }

    /// <summary>
    /// Gets the HTTP status code.
    /// </summary>
    public int HttpStatus { get; }
}

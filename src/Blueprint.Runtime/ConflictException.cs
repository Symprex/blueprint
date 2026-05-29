namespace Blueprint;

/// <summary>
/// Thrown when an operation conflicts with the current state of a resource. Translates to HTTP 409
/// with a well-known <c>conflict</c> problem type.
/// </summary>
public class ConflictException : ApiException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictException"/> class.
    /// </summary>
    /// <param name="message">A description of the conflict (used as the problem detail).</param>
    public ConflictException(string message)
        : base("Conflict", "conflict", message, 409)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictException"/> class with explicit
    /// title, type, and detail for domain-specific conflict semantics.
    /// </summary>
    /// <param name="title">A stable problem title.</param>
    /// <param name="type">A URI-reference or short token identifying the conflict type.</param>
    /// <param name="detail">An instance-specific description of the conflict.</param>
    public ConflictException(string title, string type, string detail)
        : base(title, type, detail, 409)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictException"/> class with explicit
    /// title, type, detail and an inner exception.
    /// </summary>
    /// <param name="title">A stable problem title.</param>
    /// <param name="type">A URI-reference or short token identifying the conflict type.</param>
    /// <param name="detail">An instance-specific description of the conflict.</param>
    /// <param name="innerException">The inner exception.</param>
    public ConflictException(string title, string type, string detail, Exception innerException)
        : base(title, type, detail, 409, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictException"/> class.
    /// </summary>
    /// <param name="message">A description of the conflict (used as the problem detail).</param>
    /// <param name="innerException">The inner exception.</param>
    public ConflictException(string message, Exception innerException)
        : base("Conflict", "conflict", message, 409, innerException)
    {
    }
}

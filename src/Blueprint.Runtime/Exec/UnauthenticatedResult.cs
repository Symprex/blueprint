using Blueprint.Authorization;

namespace Blueprint.Runtime.Exec;

/// <summary>
/// Indicates the request was rejected because the caller is not authenticated.
/// The HTTP host translates this to a 401 Unauthorized response.
/// </summary>
public sealed class UnauthenticatedResult : OperationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnauthenticatedResult"/> class for an anonymous caller.
    /// </summary>
    public UnauthenticatedResult()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UnauthenticatedResult"/> class for the denial an authoriser returned.
    /// </summary>
    /// <param name="failure">The denying authoriser's result.</param>
    public UnauthenticatedResult(ExecutionAllowed failure)
    {
        this.Failure = failure;
    }

    /// <summary>
    /// Gets a shared instance for an anonymous caller of an operation that does not allow one, to avoid allocating a new
    /// object per rejected request. Safe because <see cref="UnauthenticatedResult"/> is immutable.
    /// </summary>
    public static UnauthenticatedResult Instance { get; } = new();

    /// <summary>
    /// Gets the denying authoriser's result, or <see langword="null"/> for an anonymous caller.
    /// </summary>
    /// <remarks>
    /// The HTTP host writes it as legacy Blueprint's <c>UnauthorizedException(result.Message)</c>: the message as the
    /// problem's title, type <c>security_failure</c>. An anonymous caller is written as legacy's
    /// <c>UnauthorizedException("Access denied. Anonymous access is not allowed.")</c>.
    /// </remarks>
    public ExecutionAllowed? Failure { get; }

    /// <inheritdoc />
    public override bool WasSuccessful => false;

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        return Task.CompletedTask;
    }
}

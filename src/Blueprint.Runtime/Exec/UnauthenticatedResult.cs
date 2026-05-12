namespace Blueprint.Runtime.Exec;

/// <summary>
/// Indicates the request was rejected because the caller is not authenticated.
/// The HTTP host translates this to a 401 Unauthorized response.
/// </summary>
public sealed class UnauthenticatedResult : OperationResult
{
    /// <summary>
    /// Gets a shared instance to avoid allocating a new object per rejected request.
    /// Safe because <see cref="UnauthenticatedResult"/> is immutable and stateless.
    /// </summary>
    public static UnauthenticatedResult Instance { get; } = new();

    /// <inheritdoc />
    public override bool WasSuccessful => false;

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        return Task.CompletedTask;
    }
}

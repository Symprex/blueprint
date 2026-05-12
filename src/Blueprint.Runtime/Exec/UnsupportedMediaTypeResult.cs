namespace Blueprint.Runtime.Exec;

/// <summary>
/// Indicates the request was rejected because the request body content type is not supported
/// (e.g. the operation expects JSON but received XML or form data).
/// The HTTP host translates this to a 415 Unsupported Media Type response.
/// </summary>
public sealed class UnsupportedMediaTypeResult : OperationResult
{
    /// <summary>
    /// Gets a shared instance to avoid allocating a new object per rejected request.
    /// Safe because <see cref="UnsupportedMediaTypeResult"/> is immutable and stateless.
    /// </summary>
    public static UnsupportedMediaTypeResult Instance { get; } = new();

    /// <inheritdoc />
    public override bool WasSuccessful => false;

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        return Task.CompletedTask;
    }
}

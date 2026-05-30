namespace Blueprint.Runtime.Http;

/// <summary>
/// A singleton sentinel returned by generated pipelines that have already written
/// the HTTP response directly (via <see cref="Exec.PipelineHelpers.WriteOkJsonResponseAsync"/>).
/// The result executor recognises this type and performs no further I/O.
/// </summary>
/// <remarks>
/// This eliminates the per-request <see cref="Exec.OkResult{T}"/> heap allocation (~40 bytes)
/// for operations whose handler returns a simple typed value and requires no custom headers,
/// ETag support, or response transformation.
/// </remarks>
public sealed class HttpResponseAlreadyWrittenResult : OperationResult
{
    /// <summary>
    /// Gets the shared singleton instance.
    /// </summary>
    public static HttpResponseAlreadyWrittenResult Instance { get; } = new();

    private HttpResponseAlreadyWrittenResult()
    {
    }

    /// <inheritdoc />
    public override bool WasSuccessful => true;

    /// <inheritdoc />
    public override Task ExecuteAsync(Exec.ApiOperationContext context)
    {
        return Task.CompletedTask;
    }
}

using Blueprint.Runtime.Exec;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Represents a pipeline outcome where the HTTP response was already written directly
/// to the response stream (for example for binding failures or content-type rejections).
/// The result executor must not attempt to write another response.
/// </summary>
public sealed class HttpPipelineShortCircuitResult : OperationResult
{
    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        return Task.CompletedTask;
    }
}

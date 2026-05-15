using Blueprint.Runtime.Exec;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Represents an explicit HTTP-owned accepted (202) result with no response body, distinct from
/// the typed <see cref="AcceptedResult{T}"/> that carries a payload. Use this when a command has been
/// accepted for asynchronous processing and no body needs to be returned.
/// </summary>
public sealed class HttpAcceptedResult : OperationResult
{
    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        throw new NotSupportedException("HttpAcceptedResult must be translated by an HTTP result executor instead of self-executing.");
    }
}

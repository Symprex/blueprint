using Blueprint.Runtime.Exec;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Represents an explicit HTTP-owned no-content success result that stays distinct from the host-neutral
/// <see cref="Runtime.Exec.NoResultOperationResult"/> semantic shape.
/// </summary>
public sealed class HttpNoContentResult : OperationResult
{
    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        throw new NotSupportedException("HttpNoContentResult must be translated by an HTTP result executor instead of self-executing.");
    }
}

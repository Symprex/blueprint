namespace Blueprint.Runtime.Exec;

/// <summary>
/// Represents a successful operation outcome that intentionally carries no body payload.
/// </summary>
public sealed class NoResultOperationResult : OperationResult
{
    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        return Task.CompletedTask;
    }
}

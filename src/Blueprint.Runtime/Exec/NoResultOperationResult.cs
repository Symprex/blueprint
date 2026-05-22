namespace Blueprint.Runtime.Exec;

/// <summary>
/// Represents a successful operation outcome that intentionally carries no body payload.
/// Use <see cref="Instance"/> to avoid per-request allocation since this type is stateless.
/// </summary>
public sealed class NoResultOperationResult : OperationResult
{
    /// <summary>
    /// Gets a shared singleton instance. Since <see cref="NoResultOperationResult"/> is stateless,
    /// a single instance can be safely reused across all pipeline executions.
    /// </summary>
    public static NoResultOperationResult Instance { get; } = new();

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        return Task.CompletedTask;
    }
}

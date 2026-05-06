namespace Blueprint.Runtime.Exec;

/// <summary>
/// Indicates the request was rejected because the authenticated caller does not have
/// sufficient permissions for the requested resource operation.
/// The HTTP host translates this to a 403 Forbidden response.
/// </summary>
public sealed class ForbiddenResult : OperationResult
{
    /// <inheritdoc />
    public override bool WasSuccessful => false;

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        return Task.CompletedTask;
    }
}

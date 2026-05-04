using Blueprint.Runtime.Exec;

namespace Blueprint.Auditing;

/// <summary>
/// Abstraction for writing audit records after successful operation execution.
/// Implementations must persist independently of the ambient unit-of-work scope because
/// the auditing module runs after persistence has already committed.
/// </summary>
/// <remarks>
/// Auditing is best-effort: if the sink throws, the generated pipeline logs the failure
/// but still returns the successful operation result to the caller. Implementations that
/// require transactional audit guarantees should manage their own persistence boundary.
/// </remarks>
public interface IAuditSink
{
    /// <summary>
    /// Records an audit entry for a completed operation execution.
    /// </summary>
    /// <param name="context">The operation execution context (operation instance, user, service provider).</param>
    /// <param name="result">The handler result indicating the outcome.</param>
    /// <param name="cancellationToken">
    /// A cancellation token. The generated pipeline passes <see cref="System.Threading.CancellationToken.None"/>
    /// so that audit writes complete even when the HTTP request is cancelled.
    /// </param>
    /// <returns>A task that completes when the audit record is persisted.</returns>
    Task RecordAsync(ApiOperationContext context, OperationResult result, CancellationToken cancellationToken);
}

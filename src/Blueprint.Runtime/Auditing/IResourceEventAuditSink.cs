using Blueprint.Runtime.Exec;

namespace Blueprint.Auditing;

/// <summary>
/// Abstraction for recording resource-event audit entries after successful handler execution
/// returns a <see cref="ResourceEventResult"/> or <see cref="KeyOnlyResourceEventResult"/>.
/// Implementations must persist independently of the ambient unit-of-work scope.
/// </summary>
/// <remarks>
/// <para>
/// Resource-event auditing is best-effort: if the sink throws, the generated pipeline emits
/// a diagnostic activity event but still returns the successful result to the caller.
/// </para>
/// <para>
/// This contract is save-only by design. There is no read-back, no <c>GetCurrentDataAsync</c>,
/// and no <c>ChangedValues</c>. The sink receives the stable event identity and resource identity
/// and may persist them however the consumer requires.
/// </para>
/// </remarks>
public interface IResourceEventAuditSink
{
    /// <summary>
    /// Records a resource-event audit entry for a completed operation that returned a resource-event result.
    /// </summary>
    /// <param name="eventId">The stable event identity (e.g. "template.created").</param>
    /// <param name="resourceId">The stable resource identity associated with the event.</param>
    /// <param name="context">The operation execution context.</param>
    /// <param name="cancellationToken">
    /// A cancellation token. The generated pipeline passes <see cref="CancellationToken.None"/>
    /// so that audit writes complete even when the HTTP request is cancelled.
    /// </param>
    /// <returns>A task that completes when the resource-event audit record is persisted.</returns>
    Task RecordAsync(string eventId, string resourceId, ApiOperationContext context, CancellationToken cancellationToken);
}

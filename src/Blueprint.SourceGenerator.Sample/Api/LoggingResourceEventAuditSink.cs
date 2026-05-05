using Blueprint.Auditing;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// A sample <see cref="IResourceEventAuditSink"/> implementation that logs resource-event
/// audit entries using standard <see cref="ILogger"/>.
/// </summary>
/// <remarks>
/// In a real application this would persist audit records to a database, event bus,
/// or external audit service. This sample demonstrates the save-only contract and
/// how the generated pipeline invokes the sink after successful execution.
/// </remarks>
public sealed class LoggingResourceEventAuditSink(ILogger<LoggingResourceEventAuditSink> logger) : IResourceEventAuditSink
{
    /// <inheritdoc />
    public Task RecordAsync(string eventId, string resourceId, ApiOperationContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Resource event audit: EventId={EventId}, ResourceId={ResourceId}, Operation={OperationType}",
            eventId,
            resourceId,
            context.Operation.GetType().Name);

        return Task.CompletedTask;
    }
}

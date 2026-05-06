using Blueprint.Auditing;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// A sample <see cref="IAuditSink"/> implementation that logs general operation audit entries
/// using structured logging.
/// </summary>
/// <remarks>
/// In production this would persist audit records to an event store, message bus, or compliance
/// system. This sample demonstrates the best-effort audit contract: failures here do not affect
/// the caller's response.
/// </remarks>
public sealed class LoggingAuditSink(ILogger<LoggingAuditSink> logger) : IAuditSink
{
    /// <inheritdoc />
    public Task RecordAsync(ApiOperationContext context, OperationResult result, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Audit: Operation={OperationType}, User={User}, Success={Success}",
            context.Operation.GetType().Name,
            context.UserPrincipal?.Identity?.Name ?? "(anonymous)",
            result.WasSuccessful);

        return Task.CompletedTask;
    }
}

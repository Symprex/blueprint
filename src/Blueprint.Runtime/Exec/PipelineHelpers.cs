using System.Diagnostics;
using Blueprint.Auditing;
using Blueprint.Runtime.Middleware;

namespace Blueprint.Runtime.Exec;

/// <summary>
/// Shared helpers called from generated pipelines to avoid duplicating multi-line code blocks
/// across every generated executor. Each method is a direct replacement for an inline pattern
/// that the source generator previously emitted per-pipeline.
/// </summary>
/// <remarks>
/// These helpers must remain <c>public</c> because generated pipelines live in the consumer
/// assembly and reference <c>Blueprint.Runtime</c> as a normal dependency. The methods are
/// designed for the source generator to call — they are not part of the consumer-facing API.
/// </remarks>
public static class PipelineHelpers
{
    /// <summary>
    /// Invokes registered <see cref="IPropertyPopulator"/> instances in order, using an optimised
    /// single-populator fast path to avoid enumerator allocation when only one is registered.
    /// </summary>
    /// <param name="populators">The pre-resolved array of property populators (empty when none are registered).</param>
    /// <param name="context">The operation context that the populators will enrich.</param>
    /// <returns>A task that completes when all populators have run.</returns>
    public static async Task PopulatePropertiesAsync(
        IPropertyPopulator[] populators,
        ApiOperationContext context)
    {
        if (populators.Length == 1)
        {
            await populators[0].PopulateAsync(context, context.OperationCancelled).ConfigureAwait(false);
        }
        else if (populators.Length > 1)
        {
            foreach (var populator in populators)
            {
                await populator.PopulateAsync(context, context.OperationCancelled).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Executes the general audit sink with best-effort semantics: records started/succeeded/failed
    /// activity events and swallows sink exceptions. When the sink is <c>null</c> (not registered),
    /// the method returns immediately with no overhead.
    /// </summary>
    /// <param name="auditSink">The audit sink to invoke, or <c>null</c> if none is registered.</param>
    /// <param name="context">The operation context for the current pipeline execution.</param>
    /// <param name="handlerResult">The handler result to pass to the audit sink.</param>
    /// <param name="operationTags">Pre-allocated activity tags for the operation type (reused for started/succeeded events).</param>
    /// <param name="operationTypeName">The operation type name for failure event tags.</param>
    /// <returns>A task that completes when the audit sink has finished (or been skipped).</returns>
    public static async Task ExecuteAuditSinkAsync(
        IAuditSink? auditSink,
        ApiOperationContext context,
        OperationResult handlerResult,
        ActivityTagsCollection operationTags,
        string operationTypeName)
    {
        if (auditSink == null)
        {
            return;
        }

        context.Activity?.AddEvent(new ActivityEvent("blueprint.audit.started", default, operationTags));

        try
        {
            await auditSink.RecordAsync(context, handlerResult, CancellationToken.None).ConfigureAwait(false);
            context.Activity?.AddEvent(new ActivityEvent("blueprint.audit.succeeded", default, operationTags));
        }
        catch (Exception auditException)
        {
            RecordAuditFailureEvent(context.Activity, "blueprint.audit.failed", operationTypeName, auditException);
        }
    }

    /// <summary>
    /// Records a best-effort audit failure activity event using OTel semantic conventions for
    /// exception attributes. Shared between general auditing and resource-event auditing modules.
    /// </summary>
    /// <param name="activity">The current activity, or <c>null</c> if tracing is not active.</param>
    /// <param name="eventName">The activity event name (e.g. <c>blueprint.audit.failed</c>).</param>
    /// <param name="operationTypeName">The operation type name for the <c>blueprint.operation.type</c> tag.</param>
    /// <param name="exception">The exception that caused the audit failure.</param>
    /// <param name="eventId">Optional resource event ID for resource-event audit failures.</param>
    /// <param name="resourceId">Optional resource ID for resource-event audit failures.</param>
    public static void RecordAuditFailureEvent(
        Activity? activity,
        string eventName,
        string operationTypeName,
        Exception exception,
        string? eventId = null,
        string? resourceId = null)
    {
        if (activity == null)
        {
            return;
        }

        var tags = new ActivityTagsCollection
        {
            { "blueprint.operation.type", operationTypeName },
            { "exception.type", exception.GetType().FullName },
            { "exception.message", exception.Message },
            { "exception.stacktrace", exception.StackTrace },
        };

        if (eventId != null)
        {
            tags.Add("blueprint.resource_event.id", eventId);
        }

        if (resourceId != null)
        {
            tags.Add("blueprint.resource_event.resource_id", resourceId);
        }

        activity.AddEvent(new ActivityEvent(eventName, default, tags));
    }

    /// <summary>
    /// Reads and buffers the HTTP request body, validates the content type is JSON, then calls the
    /// supplied <paramref name="populate"/> delegate with the raw bytes. This extracts the ~80 lines
    /// of body-reading boilerplate (pooled buffer management, chunked-transfer fallback, content-type
    /// guard, JSON exception translation) into a single runtime method so generated pipelines emit
    /// only a one-line call instead of duplicating the pattern per operation.
    /// </summary>
    /// <param name="httpContext">The current HTTP context to read the request body from.</param>
    /// <param name="cancellationToken">Cancellation token to observe during body reads.</param>
    /// <param name="populate">
    /// A delegate that receives the buffered body bytes as a <see cref="ReadOnlySpan{T}"/> and
    /// populates the operation properties (typically via <c>Utf8JsonReader</c>). This is the only
    /// operation-specific code; all buffering/error handling is shared.
    /// </param>
    /// <returns>
    /// <c>null</c> when the body was read and populated successfully (or was empty/absent),
    /// or an <see cref="OperationResult"/> representing the failure (unsupported media type,
    /// invalid JSON, etc.).
    /// </returns>
    public static async Task<OperationResult?> ReadJsonBodyAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken,
        PopulateFromBodyDelegate populate)
    {
        // No body to read — skip silently
        if (httpContext.Request.ContentLength is null or 0 && httpContext.Request.ContentType is null)
        {
            return null;
        }

        // Content-type guard: reject non-JSON bodies early
        if (httpContext.Request.ContentType is not null &&
            !httpContext.Request.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return new UnsupportedMediaTypeResult();
        }

        // Fast path: Content-Length is known — rent a pooled buffer and read directly
        if (httpContext.Request.ContentLength.HasValue && httpContext.Request.ContentLength.Value > 0)
        {
            var bodyLength = (int)httpContext.Request.ContentLength.Value;
            var rentedBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(bodyLength);
            var totalRead = 0;

            while (totalRead < bodyLength)
            {
                var bytesRead = await httpContext.Request.Body.ReadAsync(
                    new Memory<byte>(rentedBuffer, totalRead, bodyLength - totalRead),
                    cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                totalRead += bytesRead;
            }

            if (totalRead > 0)
            {
                try
                {
                    populate(new ReadOnlySpan<byte>(rentedBuffer, 0, totalRead));
                }
                catch (System.Text.Json.JsonException)
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(rentedBuffer);
                    return new BindingFailureResult("Invalid JSON body");
                }
                catch (InvalidOperationException)
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(rentedBuffer);
                    return new BindingFailureResult("Invalid JSON body");
                }
                catch (NotSupportedException)
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(rentedBuffer);
                    return new BindingFailureResult("Invalid JSON body");
                }
            }

            System.Buffers.ArrayPool<byte>.Shared.Return(rentedBuffer);
            return null;
        }

        // Slow path: chunked transfer or unknown Content-Length — use MemoryStream
        using var bodyStream = new MemoryStream();
        await httpContext.Request.Body.CopyToAsync(bodyStream, cancellationToken).ConfigureAwait(false);

        if (bodyStream.Length > 0)
        {
            try
            {
                populate(new ReadOnlySpan<byte>(bodyStream.GetBuffer(), 0, (int)bodyStream.Length));
            }
            catch (System.Text.Json.JsonException)
            {
                return new BindingFailureResult("Invalid JSON body");
            }
            catch (InvalidOperationException)
            {
                return new BindingFailureResult("Invalid JSON body");
            }
            catch (NotSupportedException)
            {
                return new BindingFailureResult("Invalid JSON body");
            }
        }

        return null;
    }

    /// <summary>
    /// Delegate signature for the operation-specific JSON body population.
    /// Called by <see cref="ReadJsonBodyAsync"/> after the body has been buffered.
    /// </summary>
    /// <param name="bodyBytes">The raw request body bytes, ready for <see cref="System.Text.Json.Utf8JsonReader"/>.</param>
    public delegate void PopulateFromBodyDelegate(ReadOnlySpan<byte> bodyBytes);
}

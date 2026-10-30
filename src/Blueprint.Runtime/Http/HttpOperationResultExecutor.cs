using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using MvcProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;
using MvcValidationProblemDetails = Microsoft.AspNetCore.Mvc.ValidationProblemDetails;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Translates supported semantic Blueprint operation results into HTTP responses without relying on self-executing result implementations.
/// </summary>
public class HttpOperationResultExecutor
{
    private const string JsonContentType = "application/json; charset=utf-8";
    private const string ProblemJsonContentType = "application/problem+json; charset=utf-8";

    // The problem types and anonymous title legacy Blueprint's authorisation denials were written with (Signature365
    // 8b40ecc961: Errors/UnauthorizedException.cs:16, Errors/ForbiddenException.cs:15,
    // Authorisation/AuthorisationMiddlewareBuilder.cs:16).
    private const string SecurityFailureProblemType = "security_failure";
    private const string AuthorisationFailureProblemType = "unauthorized";
    private const string AnonymousAccessDeniedTitle = "Access denied. Anonymous access is not allowed.";

    // Cached empty options for the rare case where no app-owned serializer context exists.
    private static readonly JsonSerializerOptions _emptyFallbackOptions = new();

    // Pre-serialized bodies of the fixed error responses, the only copy of each.
    // Writing pre-serialized bytes eliminates per-request JSON serialization, buffer rental,
    // and Content-Length computation for the three most common error types.
    // An anonymous caller's, as legacy wrote it when the operation had no activity (see WriteDenialAndReturnTrue).
    private static readonly byte[] _unauthorizedProblemBytes = System.Text.Encoding.UTF8.GetBytes(
        """{"type":"security_failure","title":"Access denied. Anonymous access is not allowed.","status":401}""");

    private static readonly byte[] _forbiddenProblemBytes = System.Text.Encoding.UTF8.GetBytes(
        """{"type":"https://tools.ietf.org/html/rfc7231#section-6.5.3","title":"Forbidden","status":403,"detail":"You do not have permission to access this resource."}""");

    private static readonly byte[] _unsupportedMediaTypeProblemBytes = System.Text.Encoding.UTF8.GetBytes(
        """{"type":"https://tools.ietf.org/html/rfc7231#section-6.5.13","title":"Unsupported Media Type","status":415,"detail":"The request content type is not supported. Expected 'application/json'."}""");

    private static readonly BlueprintHttpOptions _defaultHttpOptions = new();

    private static readonly Task<bool> _trueTask = Task.FromResult(true);
    private static readonly Task<bool> _falseTask = Task.FromResult(false);

    // Thread-local PooledBufferWriter to avoid per-request allocation for response body serialization. Cleared before each
    // use; a write that does not finish at once takes it and leaves the thread a new one (WriteThreadBufferAsync).
    [ThreadStatic]
    private static PooledBufferWriter? _threadLocalBuffer;

    // Thread-local Utf8JsonWriter to eliminate the per-request class allocation (~96 B).
    // Reset via Utf8JsonWriter.Reset(IBufferWriter<byte>) before each use (.NET 8+ API).
    [ThreadStatic]
    private static Utf8JsonWriter? _threadLocalWriter;

    /// <summary>
    /// Attempts to execute the supplied semantic result against the HTTP response.
    /// Non-async entry point to avoid state machine allocation on the OkResult hot path
    /// (~80% of requests). Falls through to an async implementation for branches that
    /// genuinely need to await (validation, file streams, etc.).
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="operationContext">The Blueprint operation context for the current execution.</param>
    /// <param name="result">The semantic result to translate.</param>
    /// <param name="httpOptions">The active HTTP host options for the current mapping.</param>
    /// <returns><see langword="true"/> when the result was translated by this executor; otherwise <see langword="false"/>.</returns>
    public Task<bool> TryExecuteAsync(HttpContext httpContext, ApiOperationContext operationContext, OperationResult result, BlueprintHttpOptions? httpOptions = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(operationContext);
        ArgumentNullException.ThrowIfNull(result);

        httpOptions ??= _defaultHttpOptions;

        switch (result)
        {
            // OkResult first — the most common path (~80%+ of requests succeed).
            case OkResult okResult:
                return this.HandleOkResult(httpContext, operationContext, okResult);

            // Status-only results that require no I/O (zero allocation path).
            case HttpPipelineShortCircuitResult:
            case HttpResponseAlreadyWrittenResult:
            case NoResultOperationResult:
                return _trueTask;

            case HttpNoContentResult:
                httpContext.Response.StatusCode = StatusCodes.Status204NoContent;
                return _trueTask;

            case HttpAcceptedResult:
                httpContext.Response.StatusCode = StatusCodes.Status202Accepted;
                return _trueTask;

            case KeyOnlyResourceEventResult keyOnlyEvent:
                httpContext.Response.StatusCode = MapChangeTypeToStatusCode(keyOnlyEvent.ChangeType);
                return _trueTask;

            case StatusCodeResult statusCodeResult:
                httpContext.Response.StatusCode = statusCodeResult.StatusCode;
                return _trueTask;

            case RedirectResult redirectResult:
                httpContext.Response.StatusCode = redirectResult.StatusCode;
                httpContext.Response.Headers["Location"] = redirectResult.Url;
                return _trueTask;

            // Pre-serialized static error responses — Body.WriteAsync often completes synchronously.
            case UnauthenticatedResult { Failure: { } authenticationFailure }:
                return WriteDenialAndReturnTrue(httpContext, operationContext, httpOptions, StatusCodes.Status401Unauthorized, SecurityFailureProblemType, authenticationFailure.Message, preserialized: null);

            case UnauthenticatedResult:
                return WriteDenialAndReturnTrue(httpContext, operationContext, httpOptions, StatusCodes.Status401Unauthorized, SecurityFailureProblemType, AnonymousAccessDeniedTitle, _unauthorizedProblemBytes);

            case ForbiddenResult { Failure: { } authorisationFailure }:
                return WriteDenialAndReturnTrue(httpContext, operationContext, httpOptions, StatusCodes.Status403Forbidden, AuthorisationFailureProblemType, authorisationFailure.Message, preserialized: null);

            // A denial with no detail: the backstop for scope pairs with no resource authorization provider to judge them.
            case ForbiddenResult:
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                httpContext.Response.ContentType = GetErrorContentType(httpContext, httpOptions);
                httpContext.Response.ContentLength = _forbiddenProblemBytes.Length;
                return WritePreserializedAndReturnTrue(httpContext, _forbiddenProblemBytes);

            case UnsupportedMediaTypeResult:
                httpContext.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                httpContext.Response.ContentType = GetErrorContentType(httpContext, httpOptions);
                httpContext.Response.ContentLength = _unsupportedMediaTypeProblemBytes.Length;
                return WritePreserializedAndReturnTrue(httpContext, _unsupportedMediaTypeProblemBytes);

            // Everything else uses the async slow path.
            default:
                return this.TryExecuteSlowPathAsync(httpContext, operationContext, result, httpOptions);
        }
    }

    private Task<bool> HandleOkResult(HttpContext httpContext, ApiOperationContext operationContext, OkResult okResult)
    {
        if (okResult is INotModifiedResult)
        {
            httpContext.Response.StatusCode = StatusCodes.Status304NotModified;
            if (okResult.HasHeaders)
            {
                foreach (var header in okResult.Headers)
                {
                    httpContext.Response.Headers[header.Key] = header.Value;
                }
            }

            return _trueTask;
        }

        if (okResult.Content is null)
        {
            throw new InvalidOperationException("OkResult must not carry null content when translated by the HTTP executor.");
        }

        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = JsonContentType;
        if (okResult.HasHeaders)
        {
            foreach (var header in okResult.Headers)
            {
                httpContext.Response.Headers[header.Key] = header.Value;
            }
        }

        var writeTask = WriteJsonAsync(httpContext, operationContext, okResult.Content);
        if (writeTask.IsCompleted)
        {
            return _trueTask;
        }

        return AwaitWriteAndReturnTrue(writeTask);
    }

    private static async Task<bool> AwaitWriteAndReturnTrue(Task writeTask)
    {
        await writeTask.ConfigureAwait(false);
        return true;
    }

    private static Task<bool> WritePreserializedAndReturnTrue(HttpContext httpContext, byte[] bytes)
    {
        var writeTask = httpContext.Response.Body.WriteAsync(bytes, httpContext.RequestAborted);
        if (writeTask.IsCompletedSuccessfully)
        {
            return _trueTask;
        }

        return AwaitPreserializedWrite(writeTask);
    }

    private static async Task<bool> AwaitPreserializedWrite(ValueTask writeTask)
    {
        await writeTask.ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Writes an authorisation denial as legacy Blueprint wrote the <c>UnauthorizedException</c> or
    /// <c>ForbiddenException</c> its <c>AuthorisationMiddlewareBuilder</c> threw for one: the exception's type, title and
    /// status, in that order, a null title left out, and a <c>traceId</c> extension when the operation has an activity
    /// (Signature365 <c>8b40ecc961</c>, <c>Blueprint.Http/UnhandledExceptionOperationResultExecutor.cs:35-58</c>,
    /// <c>ProblemDetailsJsonConverter.cs:107-139</c>). Legacy's exception carried no detail on this path, so a denial's
    /// reason is never written.
    /// </summary>
    /// <param name="httpContext">The HTTP context.</param>
    /// <param name="operationContext">The operation's context, whose activity gives the trace id.</param>
    /// <param name="httpOptions">The active HTTP host options, which decide the content type.</param>
    /// <param name="status">The response status.</param>
    /// <param name="type">The problem's type.</param>
    /// <param name="title">The problem's title, the denial's message.</param>
    /// <param name="preserialized">The body written when there is no activity, if it is fixed.</param>
    /// <returns><see langword="true"/> when the body has been written.</returns>
    private static Task<bool> WriteDenialAndReturnTrue(HttpContext httpContext, ApiOperationContext operationContext, BlueprintHttpOptions httpOptions, int status, string type, string? title, byte[]? preserialized)
    {
        httpContext.Response.StatusCode = status;
        httpContext.Response.ContentType = GetErrorContentType(httpContext, httpOptions);

        var activity = operationContext.Activity;

        if (activity is null && preserialized is not null)
        {
            httpContext.Response.ContentLength = preserialized.Length;
            return WritePreserializedAndReturnTrue(httpContext, preserialized);
        }

        var buffer = _threadLocalBuffer ??= new PooledBufferWriter();
        buffer.Clear();

        var writer = _threadLocalWriter;
        if (writer is null)
        {
            writer = new Utf8JsonWriter(buffer);
            _threadLocalWriter = writer;
        }
        else
        {
            writer.Reset(buffer);
        }

        writer.WriteStartObject();
        writer.WriteString(_propType, type);

        if (title is not null)
        {
            writer.WriteString(_propTitle, title);
        }

        writer.WriteNumber(_propStatus, status);

        if (activity is not null)
        {
            writer.WriteString(_propTraceId, activity.TraceId.ToString());
        }

        writer.WriteEndObject();
        writer.Flush();

        httpContext.Response.ContentLength = buffer.WrittenCount;

        var writeTask = WriteThreadBufferAsync(httpContext, buffer);
        return writeTask.IsCompletedSuccessfully ? _trueTask : AwaitPreserializedWrite(writeTask);
    }

    /// <summary>
    /// Async slow path for less common result types that always require I/O (validation failures,
    /// file streams, resource events, etc.).
    /// </summary>
    private async Task<bool> TryExecuteSlowPathAsync(HttpContext httpContext, ApiOperationContext operationContext, OperationResult result, BlueprintHttpOptions httpOptions)
    {
        if (TryGetBodilessStatusCode(result, out var bodilessStatusCode))
        {
            httpContext.Response.StatusCode = bodilessStatusCode;
            return true;
        }

        switch (result)
        {
            case ValidationFailedResult validationFailedResult when DescriptorSupportsFailureResult(operationContext.Descriptor, validationFailedResult):
                await WriteValidationFailureAsync(httpContext, operationContext, validationFailedResult, httpOptions).ConfigureAwait(false);
                return true;
            case BindingFailureResult bindingFailureResult:
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                httpContext.Response.ContentType = GetErrorContentType(httpContext, httpOptions);
                await WriteJsonAsync(httpContext, operationContext, new MvcProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Bad Request",
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                    Detail = bindingFailureResult.Reason,
                }).ConfigureAwait(false);
                return true;
            case CreatedResult createdResult:
                httpContext.Response.StatusCode = StatusCodes.Status201Created;
                httpContext.Response.ContentType = JsonContentType;
                TrySetLocationHeader(httpContext, createdResult.Content);
                await WriteJsonAsync(httpContext, operationContext, createdResult.Content).ConfigureAwait(false);
                return true;
            case AcceptedResult acceptedResult:
                httpContext.Response.StatusCode = StatusCodes.Status202Accepted;
                httpContext.Response.ContentType = JsonContentType;
                await WriteJsonAsync(httpContext, operationContext, acceptedResult.Content).ConfigureAwait(false);
                return true;
            case ConflictResult conflictResult:
                httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                httpContext.Response.ContentType = JsonContentType;
                await WriteJsonAsync(httpContext, operationContext, conflictResult.Content).ConfigureAwait(false);
                return true;
            case NotFoundResult notFoundResult:
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                httpContext.Response.ContentType = JsonContentType;
                await WriteJsonAsync(httpContext, operationContext, notFoundResult.Content).ConfigureAwait(false);
                return true;
            case UnhandledExceptionResult unhandledExceptionResult when DescriptorSupportsFailureResult(operationContext.Descriptor, unhandledExceptionResult):
                await WriteUnhandledExceptionAsync(httpContext, operationContext, unhandledExceptionResult, httpOptions).ConfigureAwait(false);
                return true;
            case IDeferredResourceEventResult deferredEvent:
                httpContext.Response.StatusCode = MapChangeTypeToStatusCode(deferredEvent.ChangeType);
                httpContext.Response.ContentType = JsonContentType;
                if (deferredEvent.ChangeType == ChangeType.Created && deferredEvent.ResolvedContent is not null)
                {
                    TrySetLocationHeader(httpContext, deferredEvent.ResolvedContent);
                }

                await WriteDeferredResourceEventEnvelopeAsync(httpContext, operationContext, deferredEvent).ConfigureAwait(false);
                return true;
            case ResourceEventResult resourceEventResult:
                httpContext.Response.StatusCode = MapChangeTypeToStatusCode(resourceEventResult.ChangeType);
                httpContext.Response.ContentType = JsonContentType;
                if (resourceEventResult.ChangeType == ChangeType.Created)
                {
                    TrySetLocationHeader(httpContext, resourceEventResult.Content);
                }

                await WriteResourceEventEnvelopeAsync(httpContext, operationContext, resourceEventResult).ConfigureAwait(false);
                return true;
            case PlainTextResult plainTextResult:
                // Written exactly, with no charset appended, as legacy did (Signature365 8b40ecc961,
                // Blueprint.Http/PlainTextResult.cs:57: response.ContentType = this.ContentType, verbatim, including its
                // text/plain default).
                httpContext.Response.StatusCode = (int)plainTextResult.StatusCode;
                httpContext.Response.ContentType = plainTextResult.ContentType;
                await httpContext.Response.WriteAsync(plainTextResult.Content, httpContext.RequestAborted).ConfigureAwait(false);
                return true;
            case FileStreamResult fileStreamResult:
                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                httpContext.Response.ContentType = fileStreamResult.ContentType;
                if (fileStreamResult.FileName is not null)
                {
                    httpContext.Response.Headers.ContentDisposition = $"attachment; filename=\"{fileStreamResult.FileName}\"";
                }

                await fileStreamResult.Stream.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted).ConfigureAwait(false);
                return true;
            case ResourceEvent resourceEvent:
                // Whatever its change type, legacy sent a resource event as a 200 with the event and no Location: its
                // generated pipeline wrapped every returned value that was not an OperationResult in an OkResult.
                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                httpContext.Response.ContentType = JsonContentType;
                await WriteJsonAsync(httpContext, operationContext, resourceEvent).ConfigureAwait(false);
                return true;
            case HttpResult httpResult:
                await httpResult.ExecuteAsync(operationContext).ConfigureAwait(false);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Finds whether <paramref name="result"/> is one whose status is decided at run time — by a resource event result's
    /// change type, or by a plain text result's own status — and that status is one a response must carry no body with.
    /// </summary>
    /// <remarks>
    /// Kestrel refuses a body, and even a <c>Content-Length</c>, on a 204, 205 or 304 response, failing the request with a
    /// 500, and a client could not read one anyway. Legacy Blueprint wrote none: its result executor wrote a body only for
    /// content an operation returned, and set no status that forbids one. So such a result is written as its status alone,
    /// with no <c>Content-Type</c>, <c>Content-Length</c> or <c>Location</c>.
    /// </remarks>
    /// <param name="result">The result being translated.</param>
    /// <param name="statusCode">The status to write when the result is written without a body.</param>
    /// <returns><see langword="true"/> when the result must be written as its status alone.</returns>
    private static bool TryGetBodilessStatusCode(OperationResult result, out int statusCode)
    {
        statusCode = result switch
        {
            IDeferredResourceEventResult deferredEvent => MapChangeTypeToStatusCode(deferredEvent.ChangeType),
            ResourceEventResult resourceEventResult => MapChangeTypeToStatusCode(resourceEventResult.ChangeType),
            PlainTextResult plainTextResult => (int)plainTextResult.StatusCode,
            _ => 0,
        };

        return statusCode is StatusCodes.Status204NoContent or StatusCodes.Status205ResetContent or StatusCodes.Status304NotModified;
    }

    private static Task WriteJsonAsync(HttpContext httpContext, ApiOperationContext operationContext, object content)
    {
        return WriteJsonAsync(httpContext, operationContext.App, content);
    }

    /// <summary>
    /// Writes <paramref name="content"/> as a 200 JSON response exactly as an <see cref="OkResult"/>'s content is written,
    /// for an endpoint the HTTP host serves without an operation pipeline, such as the root-metadata document, so its body
    /// is serialised as every other response of <paramref name="app"/> is.
    /// </summary>
    /// <param name="httpContext">The HTTP context to write to.</param>
    /// <param name="app">The app whose serializer context and options apply.</param>
    /// <param name="content">The body to write.</param>
    /// <returns>A task that completes when the body has been written.</returns>
    internal static Task WriteOkJsonAsync(HttpContext httpContext, BlueprintApp app, object content)
    {
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = JsonContentType;

        return WriteJsonAsync(httpContext, app, content);
    }

    /// <summary>
    /// Writes <paramref name="content"/> as the response body, serialised as every other response of
    /// <paramref name="app"/> is, leaving the status and content type as the caller set them.
    /// </summary>
    /// <param name="httpContext">The HTTP context to write to.</param>
    /// <param name="app">The app whose serializer context and options apply.</param>
    /// <param name="content">The body to write.</param>
    /// <returns>A task that completes when the body has been written.</returns>
    internal static Task WriteJsonBodyAsync(HttpContext httpContext, BlueprintApp app, object content)
    {
        return WriteJsonAsync(httpContext, app, content);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "The HTTP executor prefers the app-owned serializer context when available and only falls back to runtime serialization for uncovered response bodies while the broader serializer-coverage migration remains in progress.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "Fallback serialization path uses runtime JSON serialization when the app-owned serializer context does not cover the response type. AOT consumers must ensure their JsonSerializerContext covers all response types.")]
    private static Task WriteJsonAsync(HttpContext httpContext, BlueprintApp app, object content)
    {
        var serializerContext = app.AppOwnedSerializerContext;
        var contentType = content.GetType();

        var jsonTypeInfo = SerializerContextTypeInfoCache.Get(app, contentType, httpContext.RequestServices);

        if (jsonTypeInfo is not null)
        {
            // Serialize to thread-local buffer to enable Content-Length (avoids chunked encoding)
            // and eliminate the internal buffer allocation within SerializeAsync.
            var buffer = _threadLocalBuffer ??= new PooledBufferWriter();
            buffer.Clear();

            var writer = _threadLocalWriter;
            if (writer is null)
            {
                writer = new Utf8JsonWriter(buffer);
                _threadLocalWriter = writer;
            }
            else
            {
                writer.Reset(buffer);
            }

            JsonSerializer.Serialize(writer, content, jsonTypeInfo);
            writer.Flush();

            httpContext.Response.ContentLength = buffer.WrittenCount;

            // WriteAsync on response body completes synchronously for small payloads in Kestrel.
            // Avoid async state machine allocation by checking IsCompletedSuccessfully.
            var writeTask = WriteThreadBufferAsync(httpContext, buffer);
            return writeTask.IsCompletedSuccessfully
                ? Task.CompletedTask
                : writeTask.AsTask();
        }
        else
        {
            var serializerOptions = serializerContext?.Options ?? _emptyFallbackOptions;
            var options = SerializerContextTypeInfoCache.GetUncoveredRootOptions(app, httpContext.RequestServices)
                ?? CreateFallbackSerializerOptions(serializerOptions);
            return JsonSerializer.SerializeAsync(httpContext.Response.Body, content, contentType, options, httpContext.RequestAborted);
        }
    }

    // Pre-computed ChangeType enum strings matching JsonStringEnumConverter<ChangeType> output (PascalCase).
    private static readonly string[] _changeTypeStrings = [nameof(ChangeType.Created), nameof(ChangeType.Updated), nameof(ChangeType.Deleted)];

    // Pre-encoded JSON property names for ResourceEvent envelope to avoid per-request UTF-8 conversion.
    private static readonly JsonEncodedText _propObject = JsonEncodedText.Encode("$object");
    private static readonly JsonEncodedText _propEventId = JsonEncodedText.Encode("eventId");
    private static readonly JsonEncodedText _propChangeType = JsonEncodedText.Encode("changeType");
    private static readonly JsonEncodedText _propCreated = JsonEncodedText.Encode("created");
    private static readonly JsonEncodedText _propResourceObject = JsonEncodedText.Encode("resourceObject");
    private static readonly JsonEncodedText _propHref = JsonEncodedText.Encode("href");
    private static readonly JsonEncodedText _propData = JsonEncodedText.Encode("data");
    private static readonly JsonEncodedText _propMetadata = JsonEncodedText.Encode("metadata");
    private static readonly JsonEncodedText _propSecureData = JsonEncodedText.Encode("secureData");
    private static readonly JsonEncodedText _propChangedValues = JsonEncodedText.Encode("changedValues");
    private static readonly JsonEncodedText _propCorrelationId = JsonEncodedText.Encode("correlationId");
    private static readonly JsonEncodedText _valEvent = JsonEncodedText.Encode("event");

    // Pre-encoded problem member names for an authorisation denial (WriteDenialAndReturnTrue).
    private static readonly JsonEncodedText _propType = JsonEncodedText.Encode("type");
    private static readonly JsonEncodedText _propTitle = JsonEncodedText.Encode("title");
    private static readonly JsonEncodedText _propStatus = JsonEncodedText.Encode("status");
    private static readonly JsonEncodedText _propTraceId = JsonEncodedText.Encode("traceId");

    /// <summary>
    /// Writes a <see cref="ResourceEventResult"/> as an event envelope directly via <see cref="Utf8JsonWriter"/>,
    /// bypassing the private <c>ResourceEventEnvelope</c> wrapper allocation and the reflection-based STJ fallback
    /// that it would trigger (since the wrapper is not registered in any <see cref="JsonSerializerContext"/>).
    /// The inner <c>data</c> payload is serialized via the app's source-generated context when available.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Dictionary<string,object> metadata bags require runtime serialization for heterogeneous values. The data payload prefers the app-owned serializer context and only falls back to reflection for uncovered types.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "Fallback serialization for metadata/secureData/changedValues dictionaries uses runtime JSON when the app-owned context does not cover these types. AOT consumers must ensure full type coverage.")]
    private static async Task WriteResourceEventEnvelopeAsync(
        HttpContext httpContext,
        ApiOperationContext operationContext,
        ResourceEventResult result)
    {
        var serializerContext = operationContext.App.AppOwnedSerializerContext;
        var serializerOptions = serializerContext?.Options ?? _emptyFallbackOptions;

        // Reuse a thread-local PooledBufferWriter to avoid per-request allocation for the
        // resource event envelope (typically < 1KB). The buffer is cleared after each use.
        var buffer = _threadLocalBuffer ??= new PooledBufferWriter();
        buffer.Clear();

        // Reuse a thread-local Utf8JsonWriter to avoid per-request class allocation (~96 B).
        var writer = _threadLocalWriter;
        if (writer is null)
        {
            writer = new Utf8JsonWriter(buffer);
            _threadLocalWriter = writer;
        }
        else
        {
            writer.Reset(buffer);
        }

        writer.WriteStartObject();

        writer.WriteString(_propObject, _valEvent);
        writer.WriteString(_propEventId, result.EventId);
        writer.WriteString(_propChangeType, _changeTypeStrings[(int)result.ChangeType]);
        writer.WriteString(_propCreated, DateTimeOffset.UtcNow);
        writer.WriteString(_propResourceObject, ApiResource.GetTypeName(result.Content.GetType()));

        // href: the content type's registered self link filled from the content, as legacy made a resource event's href
        PipelineHelpers.TryCreateResourceEventHref(operationContext, result.Content.GetType(), result.Content, out var href);

        if (href is not null)
        {
            writer.WriteString(_propHref, href);
        }

        // data: use app-owned source-generated context when available for zero-reflection serialization
        writer.WritePropertyName(_propData);

        var dataType = result.Content.GetType();
        var dataTypeInfo = SerializerContextTypeInfoCache.Get(operationContext.App, dataType, httpContext.RequestServices);

        if (dataTypeInfo is not null)
        {
            JsonSerializer.Serialize(writer, result.Content, dataTypeInfo);
        }
        else
        {
            JsonSerializer.Serialize(writer, result.Content, dataType, CreateFallbackSerializerOptions(serializerOptions));
        }

        // Nullable dictionary properties — omit when null to match the prior envelope behavior
        if (result.Metadata is not null)
        {
            writer.WritePropertyName(_propMetadata);
            JsonSerializer.Serialize(writer, result.Metadata, CreateFallbackSerializerOptions(serializerOptions));
        }

        if (result.SecureData is not null)
        {
            writer.WritePropertyName(_propSecureData);
            JsonSerializer.Serialize(writer, result.SecureData, CreateFallbackSerializerOptions(serializerOptions));
        }

        if (result.ChangedValues is not null)
        {
            writer.WritePropertyName(_propChangedValues);
            JsonSerializer.Serialize(writer, result.ChangedValues, CreateFallbackSerializerOptions(serializerOptions));
        }

        if (result.CorrelationId is not null)
        {
            writer.WriteString(_propCorrelationId, result.CorrelationId);
        }

        writer.WriteEndObject();
        writer.Flush();

        httpContext.Response.ContentLength = buffer.WrittenCount;
        await WriteThreadBufferAsync(httpContext, buffer).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes an <see cref="IDeferredResourceEventResult"/> as a wire-compatible event envelope
    /// that matches the shape produced by <see cref="WriteResourceEventEnvelopeAsync"/>.
    /// Called after the pipeline has resolved the deferred self-query and populated <see cref="IDeferredResourceEventResult.ResolvedContent"/>.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Dictionary<string,object> metadata bags require runtime serialization for heterogeneous values. The data payload prefers the app-owned serializer context and only falls back to reflection for uncovered types.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "Fallback serialization for metadata/secureData dictionaries uses runtime JSON when the app-owned context does not cover these types. AOT consumers must ensure full type coverage.")]
    private static async Task WriteDeferredResourceEventEnvelopeAsync(
        HttpContext httpContext,
        ApiOperationContext operationContext,
        IDeferredResourceEventResult result)
    {
        var serializerContext = operationContext.App.AppOwnedSerializerContext;
        var serializerOptions = serializerContext?.Options ?? _emptyFallbackOptions;

        var buffer = _threadLocalBuffer ??= new PooledBufferWriter();
        buffer.Clear();

        var writer = _threadLocalWriter;
        if (writer is null)
        {
            writer = new Utf8JsonWriter(buffer);
            _threadLocalWriter = writer;
        }
        else
        {
            writer.Reset(buffer);
        }

        writer.WriteStartObject();

        writer.WriteString(_propObject, _valEvent);
        writer.WriteString(_propEventId, result.EventId);
        writer.WriteString(_propChangeType, _changeTypeStrings[(int)result.ChangeType]);
        writer.WriteString(_propCreated, DateTimeOffset.UtcNow);

        if (result.ResolvedContent is not null)
        {
            writer.WriteString(_propResourceObject, ApiResource.GetTypeName(result.ResolvedContent.GetType()));
        }

        // href: the event's resource type's registered self link filled from the resolved content or, when the self query
        // could not load it, the self query, as legacy made a resource event's href
        string? href = null;
        var resourceType = (result as IResourceEventResourceType)?.ResourceType ?? result.ResolvedContent?.GetType();

        if (resourceType is not null)
        {
            PipelineHelpers.TryCreateResourceEventHref(operationContext, resourceType, result.ResolvedContent ?? result.SelfQuery, out href);
        }

        if (href is not null)
        {
            writer.WriteString(_propHref, href);
        }

        // data: serialize the resolved content, or, when the self query was not run or loaded nothing, write no key, as
        // legacy wrote a resource event with no data and as ResourceEvent.Data is written
        if (result.ResolvedContent is not null)
        {
            writer.WritePropertyName(_propData);

            var dataType = result.ResolvedContent.GetType();
            var dataTypeInfo = SerializerContextTypeInfoCache.Get(operationContext.App, dataType, httpContext.RequestServices);

            if (dataTypeInfo is not null)
            {
                JsonSerializer.Serialize(writer, result.ResolvedContent, dataTypeInfo);
            }
            else
            {
                JsonSerializer.Serialize(writer, result.ResolvedContent, dataType, CreateFallbackSerializerOptions(serializerOptions));
            }
        }

        if (result.Metadata is not null)
        {
            writer.WritePropertyName(_propMetadata);
            JsonSerializer.Serialize(writer, result.Metadata, CreateFallbackSerializerOptions(serializerOptions));
        }

        if (result.SecureData is not null)
        {
            writer.WritePropertyName(_propSecureData);
            JsonSerializer.Serialize(writer, result.SecureData, CreateFallbackSerializerOptions(serializerOptions));
        }

        if (result.CorrelationId is not null)
        {
            writer.WriteString(_propCorrelationId, result.CorrelationId);
        }

        writer.WriteEndObject();
        writer.Flush();

        httpContext.Response.ContentLength = buffer.WrittenCount;
        await WriteThreadBufferAsync(httpContext, buffer).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes what the thread's serialization buffer holds to the response body and, when the write does not finish at
    /// once, gives the buffer to that write: the thread's next response serializes into a new one.
    /// </summary>
    /// <remarks>
    /// A body stream may read the memory it was given until its write completes, and a compressing wrapper such as
    /// <c>BrotliStream</c> does, across awaits. While it waits the thread is free to serve another request; were that
    /// request to serialize into the same buffer, the pending write would send its bytes, one user's response carrying
    /// another's data. So memory handed to an unfinished write is never reachable from the thread again. A write that
    /// finishes at once, the usual case under Kestrel, keeps the buffer on the thread, so that path still allocates nothing.
    /// The buffer's array comes from <see cref="System.Buffers.ArrayPool{T}.Shared"/> and goes back to it (once the write
    /// completes, for one that does not finish at once), so a large body costs no array of its own on either path.
    /// </remarks>
    /// <param name="httpContext">The HTTP context whose response body is written.</param>
    /// <param name="buffer">The thread's buffer, holding the serialized body.</param>
    /// <returns>The body stream's write.</returns>
    private static ValueTask WriteThreadBufferAsync(HttpContext httpContext, PooledBufferWriter buffer)
    {
        var writeTask = buffer.WriteToAsync(httpContext.Response.Body, httpContext.RequestAborted, out var detached);

        if (detached)
        {
            _threadLocalBuffer = null;
        }

        return writeTask;
    }

    private static JsonSerializerOptions CreateFallbackSerializerOptions(JsonSerializerOptions serializerOptions)
    {
        return SerializerContextTypeInfoCache.GetFallbackOptions(serializerOptions);
    }

    private static readonly ConcurrentDictionary<Type, FrozenSet<string>> _resultTypeNamesCache = new();

    private static bool DescriptorSupportsFailureResult(ApiOperationDescriptor descriptor, OperationResult result)
    {
        if (descriptor.TryGetFeatureData<OperationFailureOutcomesFeatureData>(out var failureOutcomes) == false)
        {
            return false;
        }

        var runtimeResultTypeNames = _resultTypeNamesCache.GetOrAdd(result.GetType(), static resultType =>
        {
            // Count ancestor types first to avoid intermediate List allocation.
            var count = 0;
            for (var current = resultType; current is not null; current = current.BaseType)
            {
                if (current.FullName is not null)
                {
                    count++;
                }
            }

            var names = new string[count];
            var index = 0;
            for (var current = resultType; current is not null; current = current.BaseType)
            {
                if (current.FullName is { } fullName)
                {
                    names[index++] = fullName;
                }
            }

            return names.ToFrozenSet(StringComparer.Ordinal);
        });

        var outcomes = failureOutcomes.Outcomes;

        for (var i = 0; i < outcomes.Length; i++)
        {
            if (runtimeResultTypeNames.Contains(outcomes[i].ResultTypeName))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task WriteValidationFailureAsync(HttpContext httpContext, ApiOperationContext operationContext, ValidationFailedResult validationFailedResult, BlueprintHttpOptions httpOptions)
    {
        IDictionary<string, string[]> validationErrors;

        if (validationFailedResult.Errors is { Count: > 0 } preBuilt)
        {
            // The concrete type is Dictionary<string, string[]> which implements both
            // IReadOnlyDictionary and IDictionary. Cast directly to avoid a copy.
            // MvcValidationProblemDetails only reads the dictionary so mutation is safe.
            validationErrors = (IDictionary<string, string[]>)preBuilt;
        }
        else
        {
            // Single-pass grouping using List<string> to avoid O(n²) array expansion.
            // Final conversion to string[] happens once per member after all entries are collected.
            var grouped = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var validationResult in validationFailedResult.ValidationResults)
            {
                foreach (var entry in ExpandValidationErrorEntries(validationResult))
                {
                    if (!grouped.TryGetValue(entry.MemberName, out var list))
                    {
                        list = new List<string>(1);
                        grouped[entry.MemberName] = list;
                    }

                    if (!list.Contains(entry.ErrorMessage))
                    {
                        list.Add(entry.ErrorMessage);
                    }
                }
            }

            // Convert List<string> → string[] in a single pass for the problem details dictionary.
            var result = new Dictionary<string, string[]>(grouped.Count, StringComparer.Ordinal);

            foreach (var kvp in grouped)
            {
                result[kvp.Key] = kvp.Value.ToArray();
            }

            validationErrors = result;
        }

        var problemDetails = new MvcValidationProblemDetails(validationErrors)
        {
            Status = StatusCodes.Status422UnprocessableEntity,
            Title = "One or more validation errors occurred.",
            Type = "https://tools.ietf.org/html/rfc4918#section-11.2",
        };

        httpContext.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        httpContext.Response.ContentType = GetErrorContentType(httpContext, httpOptions);
        await WriteJsonAsync(httpContext, operationContext, problemDetails).ConfigureAwait(false);
    }

    private static IEnumerable<(string MemberName, string ErrorMessage)> ExpandValidationErrorEntries(ValidationResult validationResult)
    {
        var errorMessage = string.IsNullOrWhiteSpace(validationResult.ErrorMessage)
            ? "Validation failed."
            : validationResult.ErrorMessage;

        // Fast path: most validation results target exactly one member.
        // Avoid LINQ/HashSet/Array overhead for the common single-member case.
        var hasAny = false;
        string? first = null;

        foreach (var memberName in validationResult.MemberNames)
        {
            if (string.IsNullOrWhiteSpace(memberName))
            {
                continue;
            }

            if (!hasAny)
            {
                first = memberName;
                hasAny = true;
                continue;
            }

            // Multiple members: fall back to full deduplication
            return ExpandMultipleMemberNames(validationResult.MemberNames, first!, errorMessage);
        }

        if (!hasAny)
        {
            // Legacy keyed a result naming no member "form" (Signature365 8b40ecc961, Validation/ValidationFailures.cs:44-47).
            return [(Blueprint.Validation.ValidationFailures.FormLevelPropertyName, errorMessage)];
        }

        return [(first!, errorMessage)];
    }

    private static IEnumerable<(string MemberName, string ErrorMessage)> ExpandMultipleMemberNames(
        IEnumerable<string> memberNames,
        string first,
        string errorMessage)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { first };
        yield return (first, errorMessage);

        foreach (var memberName in memberNames)
        {
            if (!string.IsNullOrWhiteSpace(memberName) && memberName != first && seen.Add(memberName))
            {
                yield return (memberName, errorMessage);
            }
        }
    }

    /// <summary>
    /// Writes an exception an operation threw as legacy Blueprint's <c>UnhandledExceptionOperationResultExecutor</c> wrote
    /// it (Signature365 <c>8b40ecc961</c>, <c>Blueprint.Http/UnhandledExceptionOperationResultExecutor.cs:30-68</c>):
    /// an <see cref="ApiException"/> as its own status, title, type, detail and extensions, whatever its status and the
    /// exposure setting; any other exception as a 500 of type <c>unknown_error</c>, titled "Something has gone wrong, please
    /// try again" with no detail, or, with details exposed, titled with its message and detailed with its stack trace; and
    /// a <c>traceId</c> extension after the others when the operation has an activity. A null member is left out
    /// (<c>ProblemDetailsJsonConverter.cs:107-139</c>).
    /// </summary>
    private static async Task WriteUnhandledExceptionAsync(HttpContext httpContext, ApiOperationContext operationContext, UnhandledExceptionResult unhandledExceptionResult, BlueprintHttpOptions httpOptions)
    {
        var problemDetails = CreateLegacyExceptionProblem(unhandledExceptionResult.Exception, httpOptions.ExposeExceptionDetailsInErrorResponses, operationContext.Activity);
        var statusCode = problemDetails.Status!.Value;

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = GetErrorContentType(httpContext, httpOptions);
        await WriteJsonAsync(httpContext, operationContext, problemDetails).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the content type an error response is written with: <c>application/problem+json; charset=utf-8</c>, or, with
    /// <see cref="BlueprintHttpOptions.NegotiateErrorContentType"/>, what legacy Blueprint's content negotiation chose for
    /// the request's <c>Accept</c> header (<see cref="LegacyErrorContentTypeNegotiation"/>).
    /// </summary>
    /// <param name="httpContext">The HTTP context, whose request carries the <c>Accept</c> header.</param>
    /// <param name="httpOptions">The active HTTP host options.</param>
    /// <returns>The content type to write.</returns>
    internal static string GetErrorContentType(HttpContext httpContext, BlueprintHttpOptions httpOptions)
    {
        return httpOptions.NegotiateErrorContentType
            ? LegacyErrorContentTypeNegotiation.Negotiate(httpContext.Request.Headers.Accept)
            : ProblemJsonContentType;
    }

    /// <summary>
    /// Makes the problem legacy Blueprint wrote for <paramref name="exception"/> (see <see cref="WriteUnhandledExceptionAsync"/>).
    /// </summary>
    /// <param name="exception">The exception the operation threw.</param>
    /// <param name="exposeDetails">Whether exception details are exposed, which changes only a non-<see cref="ApiException"/>'s.</param>
    /// <param name="activity">The operation's activity, whose trace id is carried when there is one.</param>
    /// <returns>The problem to write.</returns>
    internal static MvcProblemDetails CreateLegacyExceptionProblem(Exception exception, bool exposeDetails, System.Diagnostics.Activity? activity)
    {
        MvcProblemDetails problemDetails;

        if (exception is ApiException apiException)
        {
            problemDetails = new MvcProblemDetails
            {
                Status = apiException.HttpStatus,
                Title = apiException.Title,
                Type = apiException.Type,
                Detail = apiException.Detail,
            };

            // Copied into this body's own dictionary, deliberately unlike legacy, whose exceptions without extensions
            // shared one static dictionary that the traceId below was written into (see AuthorisationDenialWireTests).
            foreach (var kvp in apiException.Extensions)
            {
                problemDetails.Extensions[kvp.Key] = kvp.Value;
            }
        }
        else
        {
            problemDetails = new MvcProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Type = "unknown_error",
                Title = exposeDetails ? exception.Message : "Something has gone wrong, please try again",
                Detail = exposeDetails ? exception.StackTrace : null,
            };
        }

        if (activity is not null)
        {
            problemDetails.Extensions["traceId"] = activity.TraceId.ToString();
        }

        return problemDetails;
    }

    /// <summary>
    /// Attempts to extract a self-link URL from the content and set it as the Location response header,
    /// which is required for HTTP 201 Created responses per RFC 7231 §6.3.2.
    /// </summary>
    private static void TrySetLocationHeader(HttpContext httpContext, object content)
    {
        if (content is ILinkableResource { Links: { } links } && links.TryGetValue("self", out var selfLink))
        {
            httpContext.Response.Headers.Location = selfLink.Href;
        }
    }

    private static int MapChangeTypeToStatusCode(ChangeType changeType)
    {
        return changeType switch
        {
            ChangeType.Created => StatusCodes.Status201Created,
            ChangeType.Updated => StatusCodes.Status200OK,
            ChangeType.Deleted => StatusCodes.Status204NoContent,
            _ => StatusCodes.Status200OK,
        };
    }
}

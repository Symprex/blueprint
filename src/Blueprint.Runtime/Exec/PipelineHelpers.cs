using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using Blueprint.Auditing;
using Blueprint.Http;
using Blueprint.Model;
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
    /// Maximum nested SelfQuery depth before the pipeline aborts resolution to prevent infinite loops.
    /// </summary>
    private const int MaxSelfQueryDepth = 3;

    /// <summary>
    /// Context data key used to track current SelfQuery resolution depth and prevent runaway recursion.
    /// </summary>
    private const string SelfQueryDepthKey = "__Blueprint_SelfQueryDepth";

    private static readonly ConcurrentDictionary<string, ActivityTagsCollection> _auditTagsCache = new();

    /// <summary>
    /// Cached <see cref="ApiLinkGenerator"/> instance. Since the router middleware reuses the same
    /// <c>BaseUri</c> string reference for all requests with the same scheme/host/path, a simple
    /// reference-equality check avoids allocating a new generator (~40B) on every request that
    /// populates HATEOAS links. Thread-safe via volatile read/write — worst case two threads
    /// race and one allocation is lost, but correctness is maintained.
    /// </summary>
    private static volatile ApiLinkGenerator? _cachedApiLinkGenerator;

    /// <summary>
    /// Cached <c>Task.FromResult&lt;OperationResult?&gt;(null)</c> to avoid allocating a new Task on every
    /// successful authorization fast-path return. Since <c>Task.FromResult(default(T))</c> where T is a
    /// reference type is NOT cached by the runtime (unlike <c>Task.FromResult(true/false)</c>), a shared
    /// static instance eliminates ~48 bytes per request on all authorization-enabled pipelines.
    /// Retained for companion generators that may still return this from older overloads.
    /// </summary>
    internal static readonly Task<OperationResult?> NullOperationResultTask = Task.FromResult<OperationResult?>(null);

    /// <summary>
    /// Resolves a collection of services from the DI container and materialises them into an
    /// array. Returns <see cref="Array.Empty{T}"/> when no services of the requested type are
    /// registered. Generated pipelines call this at construction time to cache singleton or
    /// scoped service arrays as instance fields, replacing the previous inline expression:
    /// <c>(GetService&lt;IEnumerable&lt;T&gt;&gt;(sp) is { } x ? x.ToArray() : Array.Empty&lt;T&gt;())</c>.
    /// </summary>
    /// <typeparam name="T">The service type to resolve.</typeparam>
    /// <param name="serviceProvider">The root or scoped service provider.</param>
    /// <returns>A materialised array of all registered implementations, or an empty array.</returns>
    public static T[] ResolveServiceArray<T>(IServiceProvider serviceProvider)
    {
        if (Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetService<IEnumerable<T>>(serviceProvider) is not { } items)
        {
            return Array.Empty<T>();
        }

        // Fast-path: DI containers often return an array or list directly — avoid a copy.
        if (items is T[] array)
        {
            return array;
        }

        return items.ToArray();
    }

    /// <summary>
    /// Appends a validation error to a lazily-initialised list, consolidating the repeated
    /// null-coalesce-then-add pattern that the source generator previously emitted inline at
    /// every fast-path validation check site. On the success path (no validation failures) the
    /// list is never allocated. Called from generated pipelines once per property that fails
    /// compile-time validation.
    /// </summary>
    /// <param name="results">
    /// A reference to the pipeline-local nullable accumulator. The list is allocated on the
    /// first failure and reused for subsequent failures within the same request.
    /// </param>
    /// <param name="errorMessage">
    /// The pre-formatted error message, typically from
    /// <c>ValidationAttribute.FormatErrorMessage(displayName)</c>.
    /// </param>
    /// <param name="memberName">The property name that failed validation.</param>
    public static void AppendValidationError(
        ref List<ValidationResult>? results,
        string errorMessage,
        string memberName)
    {
        results ??= new List<ValidationResult>();
        results.Add(new ValidationResult(errorMessage, [memberName]));
    }

    /// <summary>
    /// Resolves a handler from the scoped DI container, falling back to <c>new T()</c> when the
    /// type is not registered. Generated pipelines call this for external handlers that have a
    /// parameterless constructor — avoiding the 3-line null-check-then-construct inline pattern
    /// that was previously duplicated across every pipeline.
    /// </summary>
    /// <typeparam name="T">The handler type to resolve.</typeparam>
    /// <param name="serviceProvider">The scoped service provider for the current request.</param>
    /// <returns>The DI-resolved handler, or a new default-constructed instance.</returns>
    public static T ResolveOrCreateHandler<T>(IServiceProvider serviceProvider)
        where T : class, new()
    {
        return Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetService<T>(serviceProvider) ?? new T();
    }

    /// <summary>
    /// Creates a <see cref="ValidationFailedResult"/> and sets the OpenTelemetry status tag on
    /// the current activity. Generated pipelines call this at every validation exit point,
    /// replacing the repeated 2-line inline pattern of tag-then-return.
    /// </summary>
    /// <param name="context">The operation context (used to access the current activity).</param>
    /// <param name="validationResults">The collected validation failures.</param>
    /// <returns>The validation failure result to return from the pipeline.</returns>
    public static ValidationFailedResult CreateValidationFailure(
        ApiOperationContext context,
        List<ValidationResult> validationResults)
    {
        context.Activity?.SetTag("otel.status_code", "OK");
        return new ValidationFailedResult(validationResults);
    }

    // Thread-local Items dictionary reused across ValidationContext instances to avoid the
    // internal Dictionary<object,object?> allocation (~96B) that the ValidationContext ctor
    // creates when items is null.
    [ThreadStatic]
    private static Dictionary<object, object?>? _validationItems;

    /// <summary>
    /// Runs the <see cref="System.ComponentModel.DataAnnotations.IValidatableObject.Validate"/> tail call
    /// for operations that implement <c>IValidatableObject</c>. Extracted from per-pipeline inline code
    /// to reduce IL size across hundreds of generated pipelines — each pipeline now emits a single helper
    /// call instead of the full foreach/lazy-list/check block.
    /// </summary>
    /// <typeparam name="TOperation">The concrete operation type (constrained to <c>IValidatableObject</c>).</typeparam>
    /// <param name="context">The operation context (used for telemetry tagging on failure).</param>
    /// <param name="operation">The operation instance to validate.</param>
    /// <returns>
    /// A <see cref="ValidationFailedResult"/> when one or more validation results are failures,
    /// or <see langword="null"/> when all results are <see cref="ValidationResult.Success"/>.
    /// </returns>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "The operation type is always preserved — it is instantiated by source-generated pipeline code.")]
    public static OperationResult? ValidateIValidatableObject<TOperation>(
        ApiOperationContext context,
        TOperation operation)
        where TOperation : System.ComponentModel.DataAnnotations.IValidatableObject
    {
        var items = _validationItems ??= new Dictionary<object, object?>();
        items.Clear();
        var ivoContext = new System.ComponentModel.DataAnnotations.ValidationContext(operation, null, items);
        var ivoResults = operation.Validate(ivoContext);
        List<ValidationResult>? ivoFailures = null;

        foreach (var ivoResult in ivoResults)
        {
            if (ivoResult != ValidationResult.Success)
            {
                ivoFailures ??= new List<ValidationResult>();
                ivoFailures.Add(ivoResult);
            }
        }

        return ivoFailures is not null ? CreateValidationFailure(context, ivoFailures) : null;
    }

    /// <summary>
    /// Accumulates <see cref="System.ComponentModel.DataAnnotations.IValidatableObject.Validate"/> results
    /// into an existing validation-results list. Used when both property-level generated checks AND
    /// <c>IValidatableObject</c> are present — mirrors the .NET <c>Validator.TryValidateObject</c> behavior
    /// of collecting all errors (property + object-level) before returning a single failure.
    /// </summary>
    /// <typeparam name="TOperation">The concrete operation type (constrained to <c>IValidatableObject</c>).</typeparam>
    /// <param name="validationResults">The existing list (may be null if no property errors yet).</param>
    /// <param name="operation">The operation instance to validate.</param>
    /// <returns>The (potentially newly-allocated) list with any <c>IValidatableObject</c> failures appended.</returns>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "The operation type is always preserved — it is instantiated by source-generated pipeline code.")]
    public static List<ValidationResult>? AccumulateIValidatableObject<TOperation>(
        List<ValidationResult>? validationResults,
        TOperation operation)
        where TOperation : System.ComponentModel.DataAnnotations.IValidatableObject
    {
        var items = _validationItems ??= new Dictionary<object, object?>();
        items.Clear();
        var ivoContext = new System.ComponentModel.DataAnnotations.ValidationContext(operation, null, items);

        foreach (var ivoResult in operation.Validate(ivoContext))
        {
            if (ivoResult != ValidationResult.Success)
            {
                validationResults ??= new List<ValidationResult>();
                validationResults.Add(ivoResult);
            }
        }

        return validationResults;
    }

    /// <summary>
    /// Invokes registered <see cref="IPropertyPopulator"/> instances in order, using an optimised
    /// single-populator fast path to avoid enumerator allocation when only one is registered.
    /// </summary>
    /// <param name="populators">The pre-resolved array of property populators (empty when none are registered).</param>
    /// <param name="context">The operation context that the populators will enrich.</param>
    /// <returns>A task that completes when all populators have run.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task PopulatePropertiesAsync(
        IPropertyPopulator[] populators,
        ApiOperationContext context)
    {
        if (populators.Length == 1)
        {
            return populators[0].PopulateAsync(context, context.OperationCancelled);
        }

        return populators.Length > 1
            ? PopulatePropertiesCoreAsync(populators, context)
            : Task.CompletedTask;
    }

    /// <summary>
    /// Core async loop for multiple property populators. Separated from the fast path
    /// to avoid state machine allocation when zero or one populators are registered.
    /// </summary>
    private static async Task PopulatePropertiesCoreAsync(
        IPropertyPopulator[] populators,
        ApiOperationContext context)
    {
        foreach (var populator in populators)
        {
            await populator.PopulateAsync(context, context.OperationCancelled).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Executes the general audit sink with best-effort semantics: records started/succeeded/failed
    /// activity events and swallows sink exceptions. When the sink is <c>null</c> (not registered),
    /// the method returns immediately with no overhead.
    /// </summary>
    /// <remarks>
    /// Audit tags (<c>blueprint.operation.type</c>) are cached in a shared <see cref="ConcurrentDictionary{TKey,TValue}"/>
    /// keyed by operation name. This eliminates the need for per-pipeline <c>static readonly ActivityTagsCollection</c>
    /// fields, reducing generated code by ~2 lines per pipeline (~1,264 lines for 632 S365 operations).
    /// </remarks>
    /// <param name="auditSink">The audit sink to invoke, or <c>null</c> if none is registered.</param>
    /// <param name="context">The operation context for the current pipeline execution.</param>
    /// <param name="handlerResult">The handler result to pass to the audit sink.</param>
    /// <param name="operationTypeName">The operation type name for activity tags and failure events.</param>
    /// <returns>A task that completes when the audit sink has finished (or been skipped).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task ExecuteAuditSinkAsync(
        IAuditSink? auditSink,
        ApiOperationContext context,
        OperationResult handlerResult,
        string operationTypeName)
    {
        return auditSink != null
            ? ExecuteAuditSinkCoreAsync(auditSink, context, handlerResult, operationTypeName)
            : Task.CompletedTask;
    }

    /// <summary>
    /// Core async implementation of audit sink execution, separated from the null-check fast
    /// path to avoid allocating a state machine when no audit sink is registered.
    /// </summary>
    private static async Task ExecuteAuditSinkCoreAsync(
        IAuditSink auditSink,
        ApiOperationContext context,
        OperationResult handlerResult,
        string operationTypeName)
    {
        var operationTags = GetOrCreateAuditTags(operationTypeName);

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
    /// Returns a cached <see cref="ActivityTagsCollection"/> for the given operation type name.
    /// Tags contain a single entry: <c>blueprint.operation.type = operationTypeName</c>.
    /// The cache eliminates the need for per-pipeline <c>static readonly</c> field declarations.
    /// </summary>
    private static ActivityTagsCollection GetOrCreateAuditTags(string operationTypeName)
    {
        return _auditTagsCache.GetOrAdd(
            operationTypeName,
            static name => new ActivityTagsCollection { { "blueprint.operation.type", name } });
    }

    /// <summary>
    /// Delegate signature for the operation-specific JSON body population.
    /// Called by <see cref="ReadJsonBodyAsync{TOperation}"/> after the body has been buffered.
    /// The operation instance is passed through so the delegate can be <c>static</c> (zero allocation).
    /// </summary>
    /// <typeparam name="TOperation">The concrete operation type.</typeparam>
    /// <param name="bodyBytes">The raw request body bytes, ready for <see cref="System.Text.Json.Utf8JsonReader"/>.</param>
    /// <param name="operation">The operation instance whose body properties will be populated.</param>
    /// <param name="jsonOptions">
    /// The <see cref="System.Text.Json.JsonSerializerOptions"/> to use when deserializing complex types
    /// (e.g. NodaTime Instant, custom converters). May be <c>null</c> when no custom converters are needed.
    /// </param>
    public delegate void PopulateFromBodyDelegate<in TOperation>(ReadOnlySpan<byte> bodyBytes, TOperation operation, System.Text.Json.JsonSerializerOptions? jsonOptions);

    /// <summary>
    /// Reads and buffers the HTTP request body, validates the content type is JSON, then calls the
    /// supplied <paramref name="populate"/> delegate with the raw bytes and the <paramref name="operation"/>
    /// instance. Passing the operation through as a parameter enables the delegate to be <c>static</c>,
    /// avoiding closure allocation on every request.
    /// </summary>
    /// <typeparam name="TOperation">The concrete operation type.</typeparam>
    /// <param name="httpContext">The current HTTP context to read the request body from.</param>
    /// <param name="cancellationToken">Cancellation token to observe during body reads.</param>
    /// <param name="operation">The operation instance whose body properties will be populated by the delegate.</param>
    /// <param name="populate">
    /// A <c>static</c> delegate that receives the buffered body bytes and the operation, then populates
    /// body properties (typically via <c>Utf8JsonReader</c>). Because the operation is passed as a
    /// parameter rather than captured, the delegate is allocation-free.
    /// </param>
    /// <param name="jsonOptions">
    /// The <see cref="System.Text.Json.JsonSerializerOptions"/> to pass through to the <paramref name="populate"/>
    /// delegate for complex-type deserialization (e.g. NodaTime converters). May be <c>null</c>.
    /// </param>
    /// <returns>
    /// <c>null</c> when the body was read and populated successfully, or was empty/absent, or carried a content type no
    /// parser could read (legacy left the operation unpopulated in that case too, for every method); or an
    /// <see cref="OperationResult"/> representing a binding failure (invalid JSON, etc.).
    /// </returns>
    public static Task<OperationResult?> ReadJsonBodyAsync<TOperation>(
        HttpContext httpContext,
        CancellationToken cancellationToken,
        TOperation operation,
        PopulateFromBodyDelegate<TOperation> populate,
        System.Text.Json.JsonSerializerOptions? jsonOptions = null)
    {
        // No body to read — skip silently
        if (httpContext.Request.ContentLength is null or 0 && httpContext.Request.ContentType is null)
        {
            return Task.FromResult<OperationResult?>(null);
        }

        // Content-type guard: a non-JSON content type is a body no IBodyParser could read. Legacy read the body of every
        // non-GET method through whichever IBodyParser.CanRead matched the content type, and silently left the operation
        // unpopulated (no error, no 415 anywhere in that path) when none did, for every method alike (Signature365
        // 8b40ecc961, HttpBodyMessagePopulationSource.cs:80-86). R108 matched that for DELETE/HEAD/OPTIONS; POST/PUT/PATCH
        // still 415'd here. Match legacy's uniformity for those too: skip silently for every method this is reached for.
        if (httpContext.Request.ContentType is not null &&
            !httpContext.Request.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<OperationResult?>(null);
        }

        // Fast path: Content-Length is known — rent a pooled buffer and read directly.
        // Most requests in production have the body already buffered by Kestrel, so
        // ReadAsync completes synchronously. Avoid async state machine allocation by
        // checking IsCompletedSuccessfully on the first read.
        if (httpContext.Request.ContentLength.HasValue && httpContext.Request.ContentLength.Value > 0)
        {
            var bodyLength = (int)httpContext.Request.ContentLength.Value;
            var rentedBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(bodyLength);

            var firstRead = httpContext.Request.Body.ReadAsync(
                new Memory<byte>(rentedBuffer, 0, bodyLength),
                cancellationToken);

            if (firstRead.IsCompletedSuccessfully)
            {
                var bytesRead = firstRead.GetAwaiter().GetResult();

                // For small payloads, a single read returns the full body
                if (bytesRead >= bodyLength || bytesRead == 0)
                {
                    try
                    {
                        if (bytesRead > 0)
                        {
                            try
                            {
                                populate(new ReadOnlySpan<byte>(rentedBuffer, 0, bytesRead), operation, jsonOptions);
                            }
                            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or NotSupportedException)
                            {
                                return Task.FromResult<OperationResult?>(ToBodyBindingFailure(ex));
                            }
                        }

                        return Task.FromResult<OperationResult?>(null);
                    }
                    finally
                    {
                        System.Buffers.ArrayPool<byte>.Shared.Return(rentedBuffer);
                    }
                }

                // Partial read completed synchronously — continue with async fallback
                return ReadJsonBodyContinueAsync(httpContext, cancellationToken, operation, populate, jsonOptions, rentedBuffer, bodyLength, bytesRead);
            }

            // First read is async — fall through to full async path
            return ReadJsonBodyAsyncCore(httpContext, cancellationToken, operation, populate, jsonOptions, rentedBuffer, bodyLength, firstRead);
        }

        // Slow path: chunked transfer or unknown Content-Length — use MemoryStream
        return ReadJsonBodyChunkedAsync(httpContext, cancellationToken, operation, populate, jsonOptions);
    }

    /// <summary>
    /// Async continuation for when the first read completed synchronously but returned a partial result.
    /// </summary>
    private static async Task<OperationResult?> ReadJsonBodyContinueAsync<TOperation>(
        HttpContext httpContext,
        CancellationToken cancellationToken,
        TOperation operation,
        PopulateFromBodyDelegate<TOperation> populate,
        System.Text.Json.JsonSerializerOptions? jsonOptions,
        byte[] rentedBuffer,
        int bodyLength,
        int totalRead)
    {
        try
        {
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
                    populate(new ReadOnlySpan<byte>(rentedBuffer, 0, totalRead), operation, jsonOptions);
                }
                catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or NotSupportedException)
                {
                    return ToBodyBindingFailure(ex);
                }
            }

            return null;
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
    }

    /// <summary>
    /// Async path for when the first ReadAsync did not complete synchronously.
    /// </summary>
    private static async Task<OperationResult?> ReadJsonBodyAsyncCore<TOperation>(
        HttpContext httpContext,
        CancellationToken cancellationToken,
        TOperation operation,
        PopulateFromBodyDelegate<TOperation> populate,
        System.Text.Json.JsonSerializerOptions? jsonOptions,
        byte[] rentedBuffer,
        int bodyLength,
        ValueTask<int> firstRead)
    {
        try
        {
            var totalRead = await firstRead.ConfigureAwait(false);

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
                    populate(new ReadOnlySpan<byte>(rentedBuffer, 0, totalRead), operation, jsonOptions);
                }
                catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or NotSupportedException)
                {
                    return ToBodyBindingFailure(ex);
                }
            }

            return null;
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
    }

    /// <summary>
    /// Slow path for chunked transfer encoding or unknown Content-Length bodies.
    /// </summary>
    private static async Task<OperationResult?> ReadJsonBodyChunkedAsync<TOperation>(
        HttpContext httpContext,
        CancellationToken cancellationToken,
        TOperation operation,
        PopulateFromBodyDelegate<TOperation> populate,
        System.Text.Json.JsonSerializerOptions? jsonOptions)
    {
        using var bodyStream = new MemoryStream();
        await httpContext.Request.Body.CopyToAsync(bodyStream, cancellationToken).ConfigureAwait(false);

        if (bodyStream.Length > 0)
        {
            try
            {
                populate(new ReadOnlySpan<byte>(bodyStream.GetBuffer(), 0, (int)bodyStream.Length), operation, jsonOptions);
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or NotSupportedException)
            {
                return ToBodyBindingFailure(ex);
            }
        }

        return null;
    }

    /// <summary>
    /// Converts a JSON parsing exception into a <see cref="BindingFailureResult"/> with structured
    /// detail. For <see cref="System.Text.Json.JsonException"/> the message includes the JSON path
    /// and byte position so that callers can pinpoint malformed input without attaching a debugger.
    /// </summary>
    private static BindingFailureResult ToBodyBindingFailure(Exception ex)
    {
        if (ex is System.Text.Json.JsonException jsonEx)
        {
            var path = jsonEx.Path;
            var bytePos = jsonEx.BytePositionInLine;

            if (path is not null && bytePos.HasValue)
            {
                return new BindingFailureResult($"Invalid JSON body at '{path}' (byte {bytePos.Value}): {jsonEx.Message}");
            }

            if (path is not null)
            {
                return new BindingFailureResult($"Invalid JSON body at '{path}': {jsonEx.Message}");
            }
        }

        return new BindingFailureResult($"Invalid JSON body: {ex.GetType().Name}: {ex.Message}");
    }

    /// <summary>
    /// Loads the <see cref="Authorization.IUserAuthorisationContext"/> from the authenticated principal
    /// when an <see cref="Authorization.IUserAuthorisationContextFactory"/> is registered. Also sets
    /// OpenTelemetry activity tags for the authenticated user. This replaces the per-pipeline inline
    /// block that was previously emitted by <c>ApiAuthoriserModule</c>.
    /// </summary>
    /// <param name="context">The operation execution context.</param>
    /// <param name="httpContext">The HTTP context for the current request.</param>
    /// <param name="userContextFactory">The cached user authorization context factory, or <c>null</c> if not registered.</param>
    /// <returns>A task that completes when the user context has been loaded (or skipped).</returns>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static Task LoadUserContextAsync(
        ApiOperationContext context,
        HttpContext httpContext,
        Authorization.IUserAuthorisationContextFactory? userContextFactory)
    {
        // Fast path: no factory registered — avoid async state machine allocation entirely.
        if (userContextFactory == null)
        {
            return Task.CompletedTask;
        }

        if (httpContext.User.Identity is not System.Security.Claims.ClaimsIdentity { IsAuthenticated: true } userIdentity)
        {
            return Task.CompletedTask;
        }

        return LoadUserContextCoreAsync(context, userIdentity, userContextFactory);
    }

    private static async Task LoadUserContextCoreAsync(
        ApiOperationContext context,
        System.Security.Claims.ClaimsIdentity userIdentity,
        Authorization.IUserAuthorisationContextFactory userContextFactory)
    {
        context.UserAuthorisationContext = await userContextFactory
            .CreateContextAsync(userIdentity, context.OperationCancelled)
            .ConfigureAwait(false);

        if (context.UserAuthorisationContext != null && context.Activity != null)
        {
            var activity = context.Activity;
            activity.SetTag("enduser.id", context.UserAuthorisationContext.Id);
            activity.SetTag("enduser.account_id", context.UserAuthorisationContext.AccountId);
            context.UserAuthorisationContext.PopulateMetadata(static (key, value, act) => act.SetTag(key, value), activity);
        }
    }

    /// <summary>
    /// Saves unit-of-work changes when an <see cref="Blueprint.Persistence.IUnitOfWork"/> is registered.
    /// Extracted from per-pipeline inline code to reduce IL size across hundreds of generated pipelines.
    /// </summary>
    /// <param name="serviceProvider">The scoped service provider for the current operation.</param>
    /// <param name="cancellationToken">Cancellation token to observe during the save.</param>
    /// <returns>A task that completes when changes are saved (or immediately when no unit of work is registered).</returns>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static Task SaveUnitOfWorkAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var unitOfWork = serviceProvider.GetService<Blueprint.Persistence.IUnitOfWork>();

        return unitOfWork != null
            ? unitOfWork.SaveChangesAsync(cancellationToken)
            : Task.CompletedTask;
    }

    /// <summary>
    /// Resolves the current tenant identifier from the authenticated user's claims and stores it
    /// in the ambient <see cref="Blueprint.Tenancy.ITenantAccessor"/>. Called by the generated pipeline's
    /// tenant-resolution stage when both an <see cref="Blueprint.Tenancy.ITenantResolver"/> and
    /// <see cref="Blueprint.Tenancy.ITenantAccessor"/> are registered.
    /// </summary>
    /// <remarks>
    /// Extracted from per-pipeline inline code to reduce IL size — the 5-line tenant block appeared
    /// in every pipeline (~3,160 lines for S365's 632 operations).
    /// </remarks>
    /// <param name="tenantResolver">The tenant resolver (must not be null).</param>
    /// <param name="tenantAccessor">The tenant accessor (must not be null).</param>
    /// <param name="userPrincipal">The authenticated user's claims principal.</param>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static void ResolveTenant(
        Blueprint.Tenancy.ITenantResolver tenantResolver,
        Blueprint.Tenancy.ITenantAccessor tenantAccessor,
        ClaimsPrincipal? userPrincipal)
    {
        var tenantId = userPrincipal is not null
            ? tenantResolver.ResolveTenantId(userPrincipal)
            : null;

        tenantAccessor.Current = tenantId;

        // Enrich the current activity with the tenant ID for distributed tracing.
        // This replaces S365's Serilog IDiagnosticContext.Set("TenantId", ...) pattern
        // with the OpenTelemetry-native equivalent.
        if (tenantId is not null)
        {
            Activity.Current?.SetTag("enduser.tenant_id", tenantId);
        }
    }

    /// <summary>
    /// Resolves the tenant from the operation's scoped service provider, ensuring the
    /// <see cref="Blueprint.Tenancy.ITenantAccessor"/> and <see cref="Blueprint.Tenancy.ITenantResolver"/>
    /// come from the same DI scope as other scoped services (e.g. Marten's <c>IDocumentSession</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This overload exists because test infrastructure (e.g. <c>WebApplicationFactory.WithWebHostBuilder</c>)
    /// can create a child service provider that re-registers singleton services. The generated executor
    /// may hold references to the root provider's tenant services, but the operation's scoped services
    /// (like <c>IDocumentSession</c>) use the child provider's instances. Resolving from the scoped
    /// provider ensures both sides use the same <c>ITenantAccessor</c> instance.
    /// </para>
    /// </remarks>
    /// <param name="serviceProvider">The scoped service provider for the current operation.</param>
    /// <param name="userPrincipal">The authenticated user's claims principal.</param>
    /// <returns>
    /// The resolved <see cref="Blueprint.Tenancy.ITenantAccessor"/> for use in subsequent property
    /// assignment, or <see langword="null"/> if tenancy services are not registered.
    /// </returns>
    public static Blueprint.Tenancy.ITenantAccessor? ResolveTenantScoped(
        IServiceProvider serviceProvider,
        ClaimsPrincipal? userPrincipal)
    {
        var resolver = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetService<Blueprint.Tenancy.ITenantResolver>(serviceProvider);
        var accessor = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetService<Blueprint.Tenancy.ITenantAccessor>(serviceProvider);

        if (resolver is null || accessor is null)
        {
            return null;
        }

        ResolveTenant(resolver, accessor, userPrincipal);
        return accessor;
    }

    /// <summary>
    /// Commits queued outbox messages when an <see cref="Blueprint.Messaging.IMessageOutbox"/> is registered.
    /// Extracted from per-pipeline inline code to reduce IL size across hundreds of generated pipelines.
    /// </summary>
    /// <param name="serviceProvider">The scoped service provider for the current operation.</param>
    /// <param name="cancellationToken">Cancellation token to observe during the commit.</param>
    /// <returns>A task that completes when messages are committed (or immediately when no outbox is registered).</returns>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static Task CommitMessageOutboxAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var outbox = serviceProvider.GetService<Blueprint.Messaging.IMessageOutbox>();

        return outbox != null
            ? outbox.CommitAsync(cancellationToken)
            : Task.CompletedTask;
    }

    /// <summary>
    /// Flushes background tasks when an <see cref="Blueprint.Tasks.IBackgroundTaskScheduler"/> is registered.
    /// Extracted from per-pipeline inline code to reduce IL size across hundreds of generated pipelines.
    /// </summary>
    /// <param name="serviceProvider">The scoped service provider for the current operation.</param>
    /// <returns>A task that completes when tasks are flushed (or immediately when no scheduler is registered).</returns>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static Task FlushBackgroundTasksAsync(IServiceProvider serviceProvider)
    {
        var scheduler = serviceProvider.GetService<Blueprint.Tasks.IBackgroundTaskScheduler>();

        if (scheduler == null)
        {
            return Task.CompletedTask;
        }

        var valueTask = scheduler.RunNowAsync();

        return valueTask.IsCompletedSuccessfully
            ? Task.CompletedTask
            : valueTask.AsTask();
    }

    /// <summary>
    /// Extracted from per-pipeline inline code to reduce IL size across all HTTP-exposed
    /// operations that require authentication (~400+ in a typical S365-scale app).
    /// </summary>
    /// <param name="httpContext">The HTTP context for the current request.</param>
    /// <returns><c>null</c> when the user is authenticated; <see cref="UnauthenticatedResult.Instance"/> otherwise.</returns>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static OperationResult? CheckHttpAuthenticated(Microsoft.AspNetCore.Http.HttpContext httpContext)
    {
        return httpContext.User.Identity?.IsAuthenticated != true
            ? UnauthenticatedResult.Instance
            : null;
    }

    /// <summary>
    /// Context-aware variant of <see cref="CheckHttpAuthenticated(Microsoft.AspNetCore.Http.HttpContext)"/>
    /// that skips the authentication check when the context has <see cref="ApiOperationContext.SkipAuthorisation"/>
    /// set (e.g. nested SelfQuery execution from anonymous parent operations).
    /// </summary>
    /// <param name="httpContext">The HTTP context for the current request.</param>
    /// <param name="context">The operation context whose <c>SkipAuthorisation</c> flag is checked.</param>
    /// <returns><c>null</c> when auth is skipped or the user is authenticated; <see cref="UnauthenticatedResult.Instance"/> otherwise.</returns>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static OperationResult? CheckHttpAuthenticated(Microsoft.AspNetCore.Http.HttpContext httpContext, ApiOperationContext context)
    {
        if (context.SkipAuthorisation)
        {
            return null;
        }

        return httpContext.User.Identity?.IsAuthenticated != true
            ? UnauthenticatedResult.Instance
            : null;
    }

    /// <summary>
    /// Runs the pre-cached <see cref="Authorization.IApiAuthoriser"/> and
    /// <see cref="Authorization.IOperationAuthoriser"/> arrays against the current operation.
    /// Returns <c>null</c> when all authorisers pass, or an <see cref="UnauthenticatedResult"/>
    /// / <see cref="ForbiddenResult"/> when an authoriser denies execution.
    /// </summary>
    /// <remarks>
    /// This replaces the per-pipeline inline foreach loops that the source generator previously
    /// emitted into every authenticated pipeline's Authorisation stage. The authoriser arrays
    /// are resolved once at pipeline construction time and cached as fields, so this method
    /// receives them by reference with zero per-request allocation.
    /// </remarks>
    /// <param name="context">The current operation execution context.</param>
    /// <param name="apiAuthorisers">Pre-cached array of <see cref="Authorization.IApiAuthoriser"/> instances (combined execution and link-visibility).</param>
    /// <param name="operationAuthorisers">Pre-cached array of <see cref="Authorization.IOperationAuthoriser"/> instances (execution-only).</param>
    /// <returns><c>null</c> when all authorisers pass; otherwise an <see cref="UnauthenticatedResult"/> or <see cref="ForbiddenResult"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<OperationResult?> RunAuthorisersAsync(
        ApiOperationContext context,
        Authorization.IApiAuthoriser[] apiAuthorisers,
        Authorization.IOperationAuthoriser[] operationAuthorisers)
    {
        if (context.SkipAuthorisation)
        {
            return default;
        }

        if (apiAuthorisers.Length == 0 && operationAuthorisers.Length == 0)
        {
            return default;
        }

        return new ValueTask<OperationResult?>(RunAuthorisersCoreAsync(context, apiAuthorisers, operationAuthorisers));
    }

    private static async Task<OperationResult?> RunAuthorisersCoreAsync(
        ApiOperationContext context,
        Authorization.IApiAuthoriser[] apiAuthorisers,
        Authorization.IOperationAuthoriser[] operationAuthorisers)
    {
        for (var i = 0; i < apiAuthorisers.Length; i++)
        {
            var authoriser = apiAuthorisers[i];

            if (authoriser.AppliesTo(context.Descriptor))
            {
                var result = await authoriser.CanExecuteOperationAsync(context, context.Descriptor, context.Operation)
                    .ConfigureAwait(false);

                if (!result.IsAllowed)
                {
                    return CreateAuthoriserDenial(context, authoriser, result);
                }
            }
        }

        for (var i = 0; i < operationAuthorisers.Length; i++)
        {
            var authoriser = operationAuthorisers[i];

            if (authoriser.AppliesTo(context.Descriptor))
            {
                var result = await authoriser.CanExecuteOperationAsync(context, context.Descriptor, context.Operation)
                    .ConfigureAwait(false);

                if (!result.IsAllowed)
                {
                    return CreateAuthoriserDenial(context, authoriser, result);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The result an authoriser's denial is, carrying the denial, as legacy Blueprint's
    /// <c>AuthorisationMiddlewareBuilder.EnforceAsync</c> made it (Signature365 <c>8b40ecc961</c>,
    /// <c>Blueprint/Authorisation/AuthorisationMiddlewareBuilder.cs:24-50</c>): a <see cref="ForbiddenResult"/> for an
    /// <see cref="Authorization.ExecutionAllowedFailureType.Authorisation"/> failure, and an
    /// <see cref="UnauthenticatedResult"/> for any other, a default result's included. It is logged first, as legacy
    /// logged it: the only place the denial's reason goes.
    /// </summary>
    /// <param name="context">The operation's context.</param>
    /// <param name="authoriser">The authoriser that denied it.</param>
    /// <param name="result">The authoriser's denial.</param>
    /// <returns>The denial's result.</returns>
    private static OperationResult CreateAuthoriserDenial(ApiOperationContext context, object authoriser, Authorization.ExecutionAllowed result)
    {
        var logger = (context.ServiceProvider.GetService(typeof(ILoggerFactory)) as ILoggerFactory)?.CreateLogger(_permissionCheckLoggerCategory);

        if (logger?.IsEnabled(LogLevel.Information) == true)
        {
            _logPermissionCheckFailed(logger, result.Reason, authoriser.GetType().Name, null);
        }

        return result.FailureType == Authorization.ExecutionAllowedFailureType.Authorisation
            ? new ForbiddenResult(result)
            : new UnauthenticatedResult(result);
    }

    /// <summary>
    /// Invokes pre-filtered authoriser arrays that have already been narrowed to only those
    /// applicable to this pipeline's operation descriptor. Eliminates per-request
    /// <see cref="Authorization.IApiAuthoriser.AppliesTo"/> virtual calls since filtering was
    /// performed once at pipeline first-use and cached.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this overload when the pipeline has called
    /// <see cref="OperationExecutorShell.GetApplicableApiAuthorisers"/> and
    /// <see cref="OperationExecutorShell.GetApplicableOperationAuthorisers"/> to obtain
    /// pre-filtered arrays. The first denial short-circuits with an appropriate 401/403 result.
    /// </para>
    /// <para>
    /// This method uses a synchronous-first fast path: when all authorisers return completed tasks
    /// (e.g. <see cref="Authorization.ExecutionAllowed.YesTask"/>), no async state machine is allocated.
    /// Only falls back to async when an authoriser genuinely suspends for I/O.
    /// </para>
    /// </remarks>
    /// <param name="context">The current operation execution context.</param>
    /// <param name="apiAuthorisers">Pre-filtered <see cref="Authorization.IApiAuthoriser"/> instances that apply to this operation.</param>
    /// <param name="operationAuthorisers">Pre-filtered <see cref="Authorization.IOperationAuthoriser"/> instances that apply to this operation.</param>
    /// <returns><c>null</c> when all authorisers pass; otherwise an <see cref="UnauthenticatedResult"/> or <see cref="ForbiddenResult"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<OperationResult?> RunPreFilteredAuthorisersAsync(
        ApiOperationContext context,
        Authorization.IApiAuthoriser[] apiAuthorisers,
        Authorization.IOperationAuthoriser[] operationAuthorisers)
    {
        if (context.SkipAuthorisation)
        {
            return default;
        }

        if (apiAuthorisers.Length == 0 && operationAuthorisers.Length == 0)
        {
            return default;
        }

        // Synchronous fast path: most authorisers just check claims and return ExecutionAllowed.YesTask.
        // Avoid async state machine allocation when all authorisers complete synchronously.
        for (var i = 0; i < apiAuthorisers.Length; i++)
        {
            var task = apiAuthorisers[i].CanExecuteOperationAsync(context, context.Descriptor, context.Operation);

            if (!task.IsCompletedSuccessfully)
            {
                return new ValueTask<OperationResult?>(
                    RunPreFilteredAuthorisersSlowAsync(context, apiAuthorisers, operationAuthorisers, task, i, isApiPhase: true));
            }

            var result = task.GetAwaiter().GetResult();
            if (!result.IsAllowed)
            {
                return new ValueTask<OperationResult?>(CreateAuthoriserDenial(context, apiAuthorisers[i], result));
            }
        }

        for (var i = 0; i < operationAuthorisers.Length; i++)
        {
            var task = operationAuthorisers[i].CanExecuteOperationAsync(context, context.Descriptor, context.Operation);

            if (!task.IsCompletedSuccessfully)
            {
                return new ValueTask<OperationResult?>(
                    RunPreFilteredAuthorisersSlowAsync(context, apiAuthorisers, operationAuthorisers, task, i, isApiPhase: false));
            }

            var result = task.GetAwaiter().GetResult();
            if (!result.IsAllowed)
            {
                return new ValueTask<OperationResult?>(CreateAuthoriserDenial(context, operationAuthorisers[i], result));
            }
        }

        return default;
    }

    /// <summary>
    /// Async fallback when an authoriser did not complete synchronously. Continues from
    /// the point where the synchronous loop encountered a pending task.
    /// </summary>
    private static async Task<OperationResult?> RunPreFilteredAuthorisersSlowAsync(
        ApiOperationContext context,
        Authorization.IApiAuthoriser[] apiAuthorisers,
        Authorization.IOperationAuthoriser[] operationAuthorisers,
        Task<Authorization.ExecutionAllowed> pendingTask,
        int startIndex,
        bool isApiPhase)
    {
        // Complete the pending task
        var pendingResult = await pendingTask.ConfigureAwait(false);
        if (!pendingResult.IsAllowed)
        {
            return CreateAuthoriserDenial(context, isApiPhase ? apiAuthorisers[startIndex] : operationAuthorisers[startIndex], pendingResult);
        }

        if (isApiPhase)
        {
            // Continue remaining api authorisers
            for (var i = startIndex + 1; i < apiAuthorisers.Length; i++)
            {
                var result = await apiAuthorisers[i].CanExecuteOperationAsync(context, context.Descriptor, context.Operation)
                    .ConfigureAwait(false);

                if (!result.IsAllowed)
                {
                    return CreateAuthoriserDenial(context, apiAuthorisers[i], result);
                }
            }

            // Then all operation authorisers
            for (var i = 0; i < operationAuthorisers.Length; i++)
            {
                var result = await operationAuthorisers[i].CanExecuteOperationAsync(context, context.Descriptor, context.Operation)
                    .ConfigureAwait(false);

                if (!result.IsAllowed)
                {
                    return CreateAuthoriserDenial(context, operationAuthorisers[i], result);
                }
            }
        }
        else
        {
            // Continue remaining operation authorisers
            for (var i = startIndex + 1; i < operationAuthorisers.Length; i++)
            {
                var result = await operationAuthorisers[i].CanExecuteOperationAsync(context, context.Descriptor, context.Operation)
                    .ConfigureAwait(false);

                if (!result.IsAllowed)
                {
                    return CreateAuthoriserDenial(context, operationAuthorisers[i], result);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Checks resource authorization using the <see cref="Authorization.IResourceAuthorizationProvider"/>.
    /// Returns <see cref="ForbiddenResult.Instance"/> when the provider denies access, or <c>null</c>
    /// when the caller is authorized or the provider is not registered (permissive default).
    /// </summary>
    /// <param name="context">The current operation execution context.</param>
    /// <param name="principal">The authenticated caller's claims principal.</param>
    /// <param name="provider">The resource authorization provider, or <c>null</c> if none is registered.</param>
    /// <param name="resolvedScopes">The compile-time expanded scope pairs for this operation.</param>
    /// <param name="cancellationToken">Cancellation token for the authorization evaluation.</param>
    /// <returns><c>null</c> when authorized; <see cref="ForbiddenResult.Instance"/> when denied.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<OperationResult?> CheckResourceAuthorizationAsync(
        ApiOperationContext context,
        ClaimsPrincipal principal,
        Authorization.IResourceAuthorizationProvider? provider,
        IReadOnlyList<ResolvedScopePair> resolvedScopes,
        CancellationToken cancellationToken)
    {
        if (provider == null)
        {
            return default;
        }

        var task = provider.IsAuthorizedAsync(context, principal, resolvedScopes, cancellationToken);

        if (task.IsCompletedSuccessfully)
        {
            return new ValueTask<OperationResult?>(task.GetAwaiter().GetResult() ? null : ForbiddenResult.Instance);
        }

        return new ValueTask<OperationResult?>(CheckResourceAuthorizationCoreAsync(task));
    }

    private static async Task<OperationResult?> CheckResourceAuthorizationCoreAsync(
        Task<bool> pendingTask)
    {
        var isAuthorized = await pendingTask.ConfigureAwait(false);

        return isAuthorized ? null : ForbiddenResult.Instance;
    }

    /// <summary>
    /// Single-element overload for <see cref="CheckResourceAuthorizationAsync(ApiOperationContext, ClaimsPrincipal, Authorization.IResourceAuthorizationProvider, IReadOnlyList{ResolvedScopePair}, CancellationToken)"/>.
    /// Avoids per-request array allocation for the common case of operations with exactly one
    /// <c>[Resource]</c> attribute. The scope pair is passed by value and wrapped in a stack-allocated
    /// <see cref="SingleItemList{T}"/> that implements <see cref="IReadOnlyList{T}"/>.
    /// </summary>
    /// <param name="context">The current operation execution context.</param>
    /// <param name="principal">The authenticated caller's claims principal.</param>
    /// <param name="provider">The resource authorization provider, or <c>null</c> if none is registered.</param>
    /// <param name="singleScope">The single compile-time expanded scope pair for this operation.</param>
    /// <param name="cancellationToken">Cancellation token for the authorization evaluation.</param>
    /// <returns><c>null</c> when authorized; <see cref="ForbiddenResult.Instance"/> when denied.</returns>
    public static ValueTask<OperationResult?> CheckResourceAuthorizationAsync(
        ApiOperationContext context,
        ClaimsPrincipal principal,
        Authorization.IResourceAuthorizationProvider? provider,
        ResolvedScopePair singleScope,
        CancellationToken cancellationToken)
    {
        if (provider == null)
        {
            return default;
        }

        var scopeList = new SingleItemList<ResolvedScopePair>(singleScope);
        var task = provider.IsAuthorizedAsync(context, principal, scopeList, cancellationToken);

        if (task.IsCompletedSuccessfully)
        {
            return new ValueTask<OperationResult?>(task.GetAwaiter().GetResult() ? null : ForbiddenResult.Instance);
        }

        return new ValueTask<OperationResult?>(CheckResourceAuthorizationCoreAsync(task));
    }

    /// <summary>
    /// Executes the resource-event audit sink with best-effort semantics when the handler result
    /// is a <see cref="ResourceEventResult"/> or <see cref="KeyOnlyResourceEventResult"/>.
    /// Extracts the event/resource identity from the result, records started/succeeded/failed
    /// activity events, and swallows sink exceptions. When the sink is <c>null</c> or the result
    /// is not a resource-event type, the method returns immediately with no overhead.
    /// </summary>
    /// <remarks>
    /// The generated pipeline calls this for commands only, as legacy's <c>ResourceEventHandlerMiddlewareBuilder</c> matched
    /// commands only (<c>ResourceEventHandlerMiddlewareBuilder.cs:12-15</c>). A legacy <see cref="ResourceEvent"/> is also
    /// handed to the <see cref="IResourceEventAuditRepository"/> resolved from the operation's own services, when one is
    /// registered and the operation's type does not carry
    /// <see cref="DoNotAuditAttribute"/>, as legacy Blueprint's <c>ResourceEventHandler</c> handed it
    /// (<c>ResourceEventHandler.cs:72-77</c> at Signature365 <c>8b40ecc961</c>): stamped, with its href, and after its data
    /// has been expanded. The repository is called beside the sink, not through it, so registering either never displaces
    /// the other. Like the sink, it is best-effort: a repository that throws is recorded as a
    /// <c>blueprint.resource_event_audit.failed</c> activity event rather than failing an operation whose changes are
    /// already saved, and, through the overload given the pipeline's logger, logged at Error.
    /// </remarks>
    /// <param name="auditSink">The resource-event audit sink, or <c>null</c> if none is registered.</param>
    /// <param name="context">The operation context for the current pipeline execution.</param>
    /// <param name="handlerResult">The handler result — only processed if it is a resource-event result type.</param>
    /// <param name="operationTypeName">The operation type name for activity event tags.</param>
    /// <returns>A task that completes when the audit sink and repository have finished (or been skipped).</returns>
    public static Task ExecuteResourceEventAuditSinkAsync(
        IResourceEventAuditSink? auditSink,
        ApiOperationContext context,
        OperationResult handlerResult,
        string operationTypeName)
    {
        return ExecuteResourceEventAuditSinkAsync(auditSink, context, handlerResult, null, operationTypeName);
    }

    /// <inheritdoc cref="ExecuteResourceEventAuditSinkAsync(IResourceEventAuditSink?, ApiOperationContext, OperationResult, string)"/>
    /// <remarks>
    /// A repository that cannot be resolved, or whose <see cref="IResourceEventAuditRepository.AddAsync"/> throws, is also
    /// logged at <see cref="LogLevel.Error"/> through <paramref name="logger"/>, the operation's pipeline logger, as legacy's
    /// generated pipeline logged it: the exception escaped <c>ResourceEventHandler.HandleAsync</c> into the pipeline's catch,
    /// which logged it as <c>UnhandledException</c> (Signature365 <c>8b40ecc961</c>, for example
    /// <c>AdminPortalCloseTenantCommandExecutorPipeline.cs:141</c>, <c>:163-177</c>). The operation is not failed, which legacy
    /// did.
    /// </remarks>
    /// <param name="auditSink">The resource-event audit sink, or <c>null</c> if none is registered.</param>
    /// <param name="context">The operation context for the current pipeline execution.</param>
    /// <param name="handlerResult">The handler result — only processed if it is a resource-event result type.</param>
    /// <param name="logger">The operation's pipeline logger, which logs a failed audit repository; <c>null</c> logs nothing.</param>
    /// <param name="operationTypeName">The operation type name for activity event tags.</param>
    public static Task ExecuteResourceEventAuditSinkAsync(
        IResourceEventAuditSink? auditSink,
        ApiOperationContext context,
        OperationResult handlerResult,
        ILogger? logger,
        string operationTypeName)
    {
        // Always stamp a legacy ResourceEvent and populate its Href from the resource type's self link, even when no audit
        // sink is registered. This runs after the self query has been resolved, so Data is filled wherever it can be.
        StampResourceEvent(context, handlerResult);
        PopulateResourceEventHref(context, handlerResult);

        if (TryGetResourceEventAuditRepository(context, handlerResult, logger, operationTypeName, out var repository, out var resourceEvent))
        {
            return ExecuteResourceEventAuditRepositoryCoreAsync(auditSink, repository, resourceEvent, context, logger, operationTypeName);
        }

        return auditSink != null
            ? ExecuteResourceEventAuditSinkCoreAsync(auditSink, context, handlerResult, operationTypeName)
            : Task.CompletedTask;
    }

    /// <summary>
    /// Finds the <see cref="IResourceEventAuditRepository"/> a legacy <see cref="ResourceEvent"/> is to be handed to: the
    /// one registered in the operation's own services, unless the operation's type carries
    /// <see cref="DoNotAuditAttribute"/>, which legacy looked for on the type alone, not its base types
    /// (<c>GetAttributes&lt;DoNotAuditAttribute&gt;(false)</c>, <c>ResourceEventHandler.cs:72</c>).
    /// </summary>
    /// <remarks>
    /// A repository that cannot be resolved, because its construction throws, is recorded and logged as a failed
    /// resource-event audit as one whose <see cref="IResourceEventAuditRepository.AddAsync"/> throws is, and the event is not
    /// audited.
    /// </remarks>
    /// <param name="context">The operation context, whose services the repository is resolved from.</param>
    /// <param name="handlerResult">The handler result, audited only when it is a legacy <see cref="ResourceEvent"/>.</param>
    /// <param name="logger">The operation's pipeline logger, or <c>null</c>.</param>
    /// <param name="operationTypeName">The operation type name for the failure's activity event tags.</param>
    /// <param name="repository">The repository, when the event is to be audited.</param>
    /// <param name="resourceEvent">The event, when it is to be audited.</param>
    /// <returns><see langword="true"/> when the event is to be handed to the repository.</returns>
    private static bool TryGetResourceEventAuditRepository(
        ApiOperationContext context,
        OperationResult handlerResult,
        ILogger? logger,
        string operationTypeName,
        [NotNullWhen(true)] out IResourceEventAuditRepository? repository,
        [NotNullWhen(true)] out ResourceEvent? resourceEvent)
    {
        repository = null;
        resourceEvent = handlerResult as ResourceEvent;

        if (resourceEvent is null || context.Operation.GetType().IsDefined(typeof(DoNotAuditAttribute), inherit: false))
        {
            return false;
        }

        try
        {
            // Scoped, as AddResourceEventAuditRepository registers it: resolved per operation, never cached with the
            // pipeline.
            repository = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<IResourceEventAuditRepository>(context.ServiceProvider);
        }
        catch (Exception resourceEventAuditException)
        {
            RecordResourceEventAuditRepositoryFailure(context, resourceEvent, logger, operationTypeName, resourceEventAuditException);
        }

        return repository is not null;
    }

    /// <summary>
    /// Records a legacy <see cref="ResourceEvent"/> with the sink, when one is registered, and then hands it to the
    /// repository, recording and logging a repository that throws as a failed resource-event audit rather than rethrowing.
    /// </summary>
    private static async Task ExecuteResourceEventAuditRepositoryCoreAsync(
        IResourceEventAuditSink? auditSink,
        IResourceEventAuditRepository repository,
        ResourceEvent resourceEvent,
        ApiOperationContext context,
        ILogger? logger,
        string operationTypeName)
    {
        if (auditSink != null)
        {
            await ExecuteResourceEventAuditSinkCoreAsync(auditSink, context, resourceEvent, operationTypeName).ConfigureAwait(false);
        }

        try
        {
            await repository.AddAsync(resourceEvent).ConfigureAwait(false);
        }
        catch (Exception resourceEventAuditException)
        {
            RecordResourceEventAuditRepositoryFailure(context, resourceEvent, logger, operationTypeName, resourceEventAuditException);
        }
    }

    /// <summary>
    /// Records an audit repository's failure as a <c>blueprint.resource_event_audit.failed</c> activity event and logs it
    /// through the operation's pipeline logger with the event id, message and level legacy's pipeline logged an escaped
    /// exception with, so whatever watched legacy's logs for it still sees it: an <see cref="ApiException"/> whose
    /// <see cref="ApiException.HttpStatus"/> is below 500 at <see cref="LogLevel.Information"/> (<c>_NonException</c>,
    /// <c>EventId(4, "Non500Exception")</c>), any other at <see cref="LogLevel.Error"/> (<c>_UnhandledException</c>,
    /// <c>EventId(5, "UnhandledException")</c>) (Signature365 <c>8b40ecc961</c>,
    /// <c>AdminPortalCloseTenantCommandExecutorPipeline.cs:15-16</c>, <c>:163-176</c>).
    /// </summary>
    private static void RecordResourceEventAuditRepositoryFailure(
        ApiOperationContext context,
        ResourceEvent resourceEvent,
        ILogger? logger,
        string operationTypeName,
        Exception resourceEventAuditException)
    {
        RecordAuditFailureEvent(
            context.Activity,
            "blueprint.resource_event_audit.failed",
            operationTypeName,
            resourceEventAuditException,
            resourceEvent.EventId,
            resourceEvent.Data is IHaveResourceKey resourceKey ? resourceKey.ResourceKey ?? string.Empty : string.Empty);

        if (logger is null)
        {
            return;
        }

        if (resourceEventAuditException is ApiException { HttpStatus: < 500 })
        {
            _logResourceEventAuditRepositoryRefused(logger, resourceEventAuditException.Message, resourceEventAuditException);
        }
        else
        {
            _logResourceEventAuditRepositoryFailed(logger, resourceEventAuditException.Message, resourceEventAuditException);
        }
    }

    /// <summary>
    /// Stamps a legacy <see cref="ResourceEvent"/> with the request's correlation id and the operation that raised it, and
    /// fills its <see cref="ResourceEvent.Metadata"/>, as legacy Blueprint's <c>ResourceEventHandler</c> did for every event:
    /// whatever its change type, and whether or not it has data, a self query or a self link, or is audited. Signature365's
    /// audit stores all three.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The metadata is filled in legacy's order, each entry through the dictionary's indexer, so an entry the handler added
    /// keeps its place and takes the later value (Signature365 <c>8b40ecc961</c>, <c>ResourceEventHandler.cs:34-46</c>): the
    /// user's authorisation context's; then the request's, as legacy's <c>HttpContextMetadataProvider</c>, the provider its
    /// <c>AddHttp</c> registered, gave them (<c>HttpContextMetadataProvider.cs:18-25</c>); then every registered
    /// <see cref="IContextMetadataProvider"/>'s, in the order they were registered. Signature365 registers no provider of its
    /// own, so its events carry the user's metadata, <c>IpAddress</c> and <c>UserAgent</c>.
    /// </para>
    /// <para>
    /// Not legacy: outside a request the request's metadata is left out. Legacy's provider threw there, failing, after its
    /// save, a command whose changes were committed (<c>ApiOperationContextHttpExtensions.cs:45-53</c>). Likewise a
    /// connection with no remote address, on which legacy threw a <see cref="NullReferenceException"/>
    /// (<c>HttpRequestExtensions.cs:12</c>), is given the address a missing one gives (<see cref="ClientIpAddress"/>).
    /// </para>
    /// </remarks>
    private static void StampResourceEvent(ApiOperationContext context, OperationResult handlerResult)
    {
        if (handlerResult is ResourceEvent resourceEvent)
        {
            context.UserAuthorisationContext?.PopulateMetadata(static (key, value, target) => target.Metadata[key] = value, resourceEvent);

            resourceEvent.CorrelationId = context.Activity?.Id;
            resourceEvent.Operation = context.Operation;

            PopulateRequestMetadata(context.HttpContext, resourceEvent);

            if (context.ServiceProvider.GetService(typeof(IEnumerable<IContextMetadataProvider>)) is IEnumerable<IContextMetadataProvider> metadataProviders)
            {
                foreach (var metadataProvider in metadataProviders)
                {
                    metadataProvider.PopulateMetadata(static (key, value, target) => target.Metadata[key] = value, resourceEvent);
                }
            }
        }
    }

    /// <summary>
    /// Adds the request's metadata to a resource event as legacy's <c>HttpContextMetadataProvider</c> added it
    /// (<c>HttpContextMetadataProvider.cs:18-25</c>): its client's <c>IpAddress</c> always, and its <c>UserAgent</c> when it
    /// has the header. Nothing is added outside a request.
    /// </summary>
    private static void PopulateRequestMetadata(HttpContext? httpContext, ResourceEvent resourceEvent)
    {
        if (httpContext is null)
        {
            return;
        }

        var headers = httpContext.Request.Headers;

        // Null when the connection has no remote address and every address the request was forwarded for is private; legacy
        // stored whatever it was given.
        resourceEvent.Metadata["IpAddress"] = ClientIpAddress.Get(
            httpContext.Connection.RemoteIpAddress?.ToString(),
            headers["X-Forwarded-For"].ToString())!;

        if (headers.ContainsKey("User-Agent"))
        {
            resourceEvent.Metadata["UserAgent"] = headers["User-Agent"].ToString();
        }
    }

    /// <summary>
    /// Sets <see cref="ResourceEvent.Href"/> as legacy Blueprint's <c>ResourceEventHandler</c> did: the registered
    /// <c>self</c> link of the event's <see cref="ResourceEvent.ResourceType"/>, filled from <see cref="ResourceEvent.Data"/>
    /// or, when there is none, <see cref="ResourceEvent.SelfQuery"/>, under the request's base URI. When no self link is
    /// registered, or the operation is not running in a request, the href is left as the handler left it.
    /// </summary>
    /// <remarks>
    /// Neither the resource's own <c>$links</c> nor the operation's URL is consulted, as legacy consulted neither: a self
    /// link on the data can be hidden by link authorisation or relative, and the operation's URL is a different resource's
    /// (a collection's, for a create). Signature365 stores the href with every audited event.
    /// </remarks>
    private static void PopulateResourceEventHref(ApiOperationContext context, OperationResult handlerResult)
    {
        if (handlerResult is not ResourceEvent resourceEvent)
        {
            return;
        }

        // Legacy logged, as a warning, an event whose resource type has no self link to make its href from.
        if (context.App.GetLinkFor(resourceEvent.ResourceType, "self") is null)
        {
            if (Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<ILogger<ResourceEvent>>(context.ServiceProvider) is { } logger)
            {
                _logResourceEventHasNoSelfLink(logger, resourceEvent.ResourceType, null);
            }

            return;
        }

        if (TryCreateResourceEventHref(context, resourceEvent.ResourceType, resourceEvent.Data ?? resourceEvent.SelfQuery, out var href))
        {
            resourceEvent.Href = href;
        }
    }

    /// <summary>
    /// Makes a resource event's href as legacy Blueprint's <c>ResourceEventHandler</c> did: <c>CreateUrl</c> of the
    /// registered <c>self</c> link of <paramref name="resourceType"/>, its placeholders filled from <paramref name="source"/>,
    /// under the request's base URI — <see langword="null"/> when a placeholder cannot be filled.
    /// </summary>
    /// <remarks>
    /// Only the base URI the router set for the request is used. Outside a request the link generator registered for the
    /// host guesses one (<c>https://localhost/</c>, without the API's base path), and an href made from it would be stored
    /// with the audited event as though it were real; legacy had no base URI outside a request either.
    /// </remarks>
    /// <param name="context">The operation context, whose app and base URI make the href.</param>
    /// <param name="resourceType">The resource type the event belongs to, whose self link makes the href.</param>
    /// <param name="source">The object whose properties fill the self link's placeholders: the event's data, else its self query.</param>
    /// <param name="href">The href, which is <see langword="null"/> when a placeholder cannot be filled.</param>
    /// <returns><see langword="false"/> when no self link is registered for the type or there is no request base URI.</returns>
    internal static bool TryCreateResourceEventHref(ApiOperationContext context, Type resourceType, object? source, out string? href)
    {
        var selfLink = context.App.GetLinkFor(resourceType, "self");
        var linkGenerator = GetRequestLinkGenerator(context);

        if (selfLink is null || linkGenerator is null)
        {
            href = null;
            return false;
        }

        href = linkGenerator.CreateUrl(selfLink, source);
        return true;
    }

    /// <summary>
    /// Core async implementation of resource-event audit sink execution. Extracts event/resource
    /// identity from the result, records activity events, and swallows sink exceptions. Separated
    /// from the null-check fast path to avoid state machine allocation when no sink is registered.
    /// </summary>
    private static async Task ExecuteResourceEventAuditSinkCoreAsync(
        IResourceEventAuditSink auditSink,
        ApiOperationContext context,
        OperationResult handlerResult,
        string operationTypeName)
    {
        string? eventId;
        string? resourceId;

        if (handlerResult is ResourceEventResult reResult)
        {
            eventId = reResult.EventId;
            resourceId = reResult.ResourceId;
        }
        else if (handlerResult is KeyOnlyResourceEventResult koResult)
        {
            eventId = koResult.EventId;
            resourceId = koResult.ResourceId;
        }
        else if (handlerResult is IDeferredResourceEventResult deferredResult)
        {
            eventId = deferredResult.EventId;
            resourceId = deferredResult.ResourceId ?? string.Empty;
        }
        else if (handlerResult is ResourceEvent legacyResult)
        {
            eventId = legacyResult.EventId;
            resourceId = legacyResult.Data is IHaveResourceKey rk ? rk.ResourceKey ?? string.Empty : string.Empty;
        }
        else
        {
            return;
        }

        var activity = context.Activity;

        activity?.AddEvent(new ActivityEvent(
            "blueprint.resource_event_audit.started",
            default,
            new ActivityTagsCollection
            {
                { "blueprint.operation.type", operationTypeName },
                { "blueprint.resource_event.id", eventId },
                { "blueprint.resource_event.resource_id", resourceId },
            }));

        try
        {
            await auditSink.RecordAsync(eventId, resourceId, handlerResult, context, CancellationToken.None).ConfigureAwait(false);

            activity?.AddEvent(new ActivityEvent(
                "blueprint.resource_event_audit.succeeded",
                default,
                new ActivityTagsCollection
                {
                    { "blueprint.operation.type", operationTypeName },
                    { "blueprint.resource_event.id", eventId },
                }));
        }
        catch (Exception resourceEventAuditException)
        {
            RecordAuditFailureEvent(activity, "blueprint.resource_event_audit.failed", operationTypeName, resourceEventAuditException, eventId, resourceId);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  ResourceEvent SelfQuery resolution — executes a nested query to
    //  populate Data when handlers return ResourceEvent with SelfQuery.
    // ────────────────────────────────────────────────────────────────────

    private static readonly Action<ILogger, string, string, Exception?> _logSelfQueryResolved =
        LoggerMessage.Define<string, string>(
            LogLevel.Debug,
            new EventId(30, "ResourceEventSelfQueryResolved"),
            "Resolved ResourceEvent SelfQuery for {OperationType} via {SelfQueryType}");

    private static readonly Action<ILogger, Type, Exception?> _logResourceEventHasNoSelfLink =
        LoggerMessage.Define<Type>(
            LogLevel.Warning,
            new EventId(32, "ResourceEventHasNoSelfLink"),
            "No self link exists. Href property of the resource event of type {ResourceType} will not be populated");

    // Legacy's generated pipeline's _UnhandledException, which logged an audit repository's failure when it escaped
    // ResourceEventHandler.HandleAsync into the pipeline's catch (Signature365 8b40ecc961,
    // AdminPortalCloseTenantCommandExecutorPipeline.cs:173).
    private static readonly Action<ILogger, string, Exception?> _logResourceEventAuditRepositoryFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(5, "UnhandledException"),
            "An unhandled exception has occurred with message {ExceptionMessage}");

    // Legacy logged every denial through ILogger<AuthorisationMiddlewareBuilder> before throwing (Signature365 8b40ecc961,
    // Blueprint/Authorisation/AuthorisationMiddlewareBuilder.cs:26, :31-38), its reason's only outlet.
    private static readonly string _permissionCheckLoggerCategory = "Blueprint.Authorisation.AuthorisationMiddlewareBuilder";

    private static readonly Action<ILogger, string, string, Exception?> _logPermissionCheckFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            default,
            "Permission check failed. reason={0} authoriser={1}");

    // Legacy's pipeline logged an escaped non-5xx ApiException as _NonException instead (Signature365 8b40ecc961,
    // AdminPortalCloseTenantCommandExecutorPipeline.cs:15, :165-169).
    private static readonly Action<ILogger, string, Exception?> _logResourceEventAuditRepositoryRefused =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(4, "Non500Exception"),
            "A non-5xx ApiException has occurred with message {ExceptionMessage}");

    private static readonly Action<ILogger, string, string, Exception?> _logSelfQueryFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(31, "ResourceEventSelfQueryFailed"),
            "SelfQuery resolution failed for {OperationType} with query type {SelfQueryType}; Data will remain null");

    /// <summary>
    /// If the handler result is a <see cref="ResourceEvent"/> with a <see cref="ResourceEvent.SelfQuery"/>
    /// and null <see cref="ResourceEvent.Data"/>, or a <see cref="DeferredResourceEventResult{T}"/>,
    /// executes the query through the <paramref name="executor"/> using a nested context that preserves
    /// the parent's authentication, tenant, and request state. Also stamps
    /// <see cref="ResourceEvent.CorrelationId"/> from the current <see cref="Activity"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the compile-time replacement for S365's <c>ResourceEventHandler</c> middleware.
    /// The generated pipeline calls this method in the PostExecution stage, before link population
    /// and before audit, so that the populated Data is available for HATEOAS self-link generation
    /// and for audit sink recording.
    /// </para>
    /// <para>
    /// Resolution is best-effort: if the SelfQuery's operation is not registered, the nested
    /// execution returns a non-success result, or an exception occurs, Data is left null and a
    /// warning is logged. The outer pipeline still succeeds.
    /// </para>
    /// <para>
    /// A recursion guard prevents runaway loops when a SelfQuery inadvertently triggers another
    /// ResourceEvent with its own SelfQuery.
    /// </para>
    /// <para>
    /// A legacy <c>ResourceEvent</c> that reports a deletion is not resolved, as legacy's handler did not resolve it: a
    /// deleted resource cannot be loaded. It keeps no <c>Data</c>, and its href is made from its SelfQuery.
    /// </para>
    /// <para>
    /// Both legacy <c>ResourceEvent</c> (mutates <c>Data</c> in-place) and new-style
    /// <c>DeferredResourceEventResult</c> (populates <see cref="IDeferredResourceEventResult.ResolvedContent"/>)
    /// are handled by in-place mutation so the pipeline's <c>handlerResult</c> variable type is preserved.
    /// </para>
    /// </remarks>
    /// <param name="executor">The app executor for dispatching the nested query. May be null if DI did not register one.</param>
    /// <param name="context">The current pipeline execution context.</param>
    /// <param name="handlerResult">The handler result to inspect and potentially enrich.</param>
    /// <param name="logger">The pipeline logger for structured diagnostics.</param>
    /// <param name="operationTypeName">The operation type name for log messages.</param>
    /// <returns>A task that completes when resolution has finished (or been skipped).</returns>
    public static Task ResolveResourceEventSelfQueryAsync(
        BlueprintAppExecutor? executor,
        ApiOperationContext context,
        OperationResult handlerResult,
        ILogger logger,
        string operationTypeName)
    {
        MarkHandedOnResourceEventData(handlerResult);

        if (executor is null)
        {
            return Task.CompletedTask;
        }

        // Legacy ResourceEvent path: mutates Data in-place
        if (handlerResult is ResourceEvent { SelfQuery: not null, Data: null } resourceEvent)
        {
            // Legacy's ResourceEventHandler did not load a deleted resource ("cannot, obviously, get for a deleted
            // record"): its self query may fail or find nothing. The event keeps no data, and its href is made from the
            // self query. It is stamped with every other event, after resolution (StampResourceEvent).
            if (resourceEvent.ChangeType == ResourceEventChangeType.Deleted)
            {
                return Task.CompletedTask;
            }

            return ResolveResourceEventSelfQueryCoreAsync(executor, context, resourceEvent, logger, operationTypeName);
        }

        // New DeferredResourceEventResult<T> path: populates ResolvedContent in-place
        if (handlerResult is IDeferredResourceEventResult deferred)
        {
            return ResolveDeferredResourceEventCoreAsync(executor, context, deferred, logger, operationTypeName);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Marks the data of a returned <see cref="ResourceEvent"/> as not this pipeline's self query's, so the pipeline's
    /// expansion expands it.
    /// </summary>
    /// <remarks>
    /// Data an event already has when a pipeline reaches it was not loaded by that pipeline's self query, even when another
    /// pipeline's was: an outer handler handing on the event an inner operation returned. Legacy's
    /// <c>ResourceExpandMiddleware</c> expanded such data in the outer pipeline, as it expanded any data the returned event
    /// had, in every operation (Signature365 <c>8b40ecc961</c>, <c>Common/Api/ResourceExpandMiddleware.cs:21-24</c>,
    /// <c>:53-84</c>). A query's pipeline, which runs no self query, calls this on its own; a command's calls it through
    /// <see cref="ResolveResourceEventSelfQueryAsync"/>.
    /// </remarks>
    /// <param name="handlerResult">The handler result.</param>
    public static void MarkHandedOnResourceEventData(OperationResult handlerResult)
    {
        if (handlerResult is ResourceEvent { IsDataFromSelfQuery: true, Data: not null } handedOn)
        {
            handedOn.IsDataFromSelfQuery = false;
        }
    }

    /// <summary>
    /// Core async implementation of SelfQuery resolution, separated from the fast-path guard
    /// to avoid state machine allocation when the handler result is not a SelfQuery-bearing
    /// <see cref="ResourceEvent"/>.
    /// </summary>
    private static async Task ResolveResourceEventSelfQueryCoreAsync(
        BlueprintAppExecutor executor,
        ApiOperationContext context,
        ResourceEvent resourceEvent,
        ILogger logger,
        string operationTypeName)
    {
        // Recursion guard: prevent infinite loops when nested queries also return ResourceEvent+SelfQuery
        var depth = 0;
        if (context.HasData && context.Data.TryGetValue(SelfQueryDepthKey, out var depthObj) && depthObj is int d)
        {
            depth = d;
        }

        if (depth >= MaxSelfQueryDepth)
        {
            _logSelfQueryFailed(logger, operationTypeName, resourceEvent.SelfQuery!.GetType().Name, null);
            return;
        }

        // Stamp correlation ID from current activity before resolution so audit sinks see it
        resourceEvent.CorrelationId ??= context.Activity?.Id;

        // Stamp the operation reference so audit sinks can inspect the triggering command
        resourceEvent.Operation ??= context.Operation;

        var selfQuery = resourceEvent.SelfQuery!;
        var selfQueryTypeName = selfQuery.GetType().Name;

        try
        {
            // Preflight: verify the SelfQuery operation is registered before creating a nested context.
            // Use TryFindOperation (not FindOperation) to avoid throwing on unregistered operations.
            if (!executor.App.TryFindOperation(selfQuery.GetType(), out _))
            {
                _logSelfQueryFailed(logger, operationTypeName, selfQueryTypeName, null);
                return;
            }

            // The SelfQuery executes the target query through the full pipeline. If the parent
            // command wrote data via IUnitOfWork/IDocumentSession, that data may not yet be
            // committed (SaveUnitOfWork runs later in the success-guarded PostExecution phase).
            // Flush pending persistence so the nested query can read the just-written data.
            await SaveUnitOfWorkAsync(context.ServiceProvider, context.OperationCancelled).ConfigureAwait(false);

            // Create a nested context that inherits auth, tenant, and request state from the parent
            var nestedContext = context.CreateNested(selfQuery, skipAuthorisation: true);

            // Track recursion depth in the shared Data dictionary
            nestedContext.Data[SelfQueryDepthKey] = depth + 1;

            var result = await executor.ExecuteAsync(nestedContext).ConfigureAwait(false);

            if (result is OkResult okResult && okResult.Content is not null)
            {
                resourceEvent.Data = okResult.Content;
                resourceEvent.IsDataFromSelfQuery = true;

                _logSelfQueryResolved(logger, operationTypeName, selfQueryTypeName, null);
            }
            else
            {
                logger.LogWarning(
                    "SelfQuery for {OperationType} with query {SelfQueryType} returned {ResultType} instead of OkResult with content",
                    operationTypeName,
                    selfQueryTypeName,
                    result?.GetType().Name ?? "null");
                _logSelfQueryFailed(logger, operationTypeName, selfQueryTypeName, null);
            }
        }
        catch (Exception ex)
        {
            _logSelfQueryFailed(logger, operationTypeName, selfQueryTypeName, ex);

            context.Activity?.AddEvent(new ActivityEvent(
                "blueprint.resource_event.self_query_failed",
                default,
                new ActivityTagsCollection
                {
                    { "blueprint.operation.type", operationTypeName },
                    { "blueprint.self_query.type", selfQueryTypeName },
                    { "exception.message", ex.Message },
                }));
        }
    }

    /// <summary>
    /// Resolves a <see cref="DeferredResourceEventResult{T}"/> by executing its self-query through the pipeline
    /// and storing the resolved content on the instance via <see cref="IDeferredResourceEventResult.SetResolvedContent"/>.
    /// </summary>
    private static async Task ResolveDeferredResourceEventCoreAsync(
        BlueprintAppExecutor executor,
        ApiOperationContext context,
        IDeferredResourceEventResult deferred,
        ILogger logger,
        string operationTypeName)
    {
        // Recursion guard
        var depth = 0;
        if (context.HasData && context.Data.TryGetValue(SelfQueryDepthKey, out var depthObj) && depthObj is int d)
        {
            depth = d;
        }

        if (depth >= MaxSelfQueryDepth)
        {
            _logSelfQueryFailed(logger, operationTypeName, deferred.SelfQuery.GetType().Name, null);
            return;
        }

        // Stamp correlation ID from current activity before resolution
        deferred.CorrelationId ??= context.Activity?.Id;

        var selfQuery = deferred.SelfQuery;
        var selfQueryTypeName = selfQuery.GetType().Name;

        try
        {
            if (!executor.App.TryFindOperation(selfQuery.GetType(), out _))
            {
                _logSelfQueryFailed(logger, operationTypeName, selfQueryTypeName, null);
                return;
            }

            // Flush pending persistence so the nested query can read just-written data
            await SaveUnitOfWorkAsync(context.ServiceProvider, context.OperationCancelled).ConfigureAwait(false);

            var nestedContext = context.CreateNested((IOperation)selfQuery, skipAuthorisation: true);
            nestedContext.Data[SelfQueryDepthKey] = depth + 1;

            var result = await executor.ExecuteAsync(nestedContext).ConfigureAwait(false);

            if (result is OkResult okResult && okResult.Content is not null)
            {
                _logSelfQueryResolved(logger, operationTypeName, selfQueryTypeName, null);

                // Populate the deferred instance in-place so downstream modules can consume it
                deferred.SetResolvedContent(okResult.Content);
            }
            else
            {
                logger.LogWarning(
                    "SelfQuery for {OperationType} with query {SelfQueryType} returned {ResultType} instead of OkResult with content",
                    operationTypeName,
                    selfQueryTypeName,
                    result?.GetType().Name ?? "null");
                _logSelfQueryFailed(logger, operationTypeName, selfQueryTypeName, null);
            }
        }
        catch (Exception ex)
        {
            _logSelfQueryFailed(logger, operationTypeName, selfQueryTypeName, ex);

            context.Activity?.AddEvent(new ActivityEvent(
                "blueprint.resource_event.self_query_failed",
                default,
                new ActivityTagsCollection
                {
                    { "blueprint.operation.type", operationTypeName },
                    { "blueprint.self_query.type", selfQueryTypeName },
                    { "exception.message", ex.Message },
                }));
        }
    }

    /// <summary>
    /// Populates HATEOAS links on the operation result. When <see cref="ApiOperationContext.BaseUri"/>
    /// is available (set by the HTTP router), constructs <see cref="Blueprint.Http.ApiLinkGenerator"/>
    /// inline — avoiding the per-request scoped DI resolution of <c>IApiLinkGenerator</c> that
    /// previously re-read <c>IHttpContextAccessor</c> and recomputed the base URI every request.
    /// Falls back to DI resolution when BaseUri is unavailable (non-HTTP host or test contexts).
    /// Skips link population entirely for <see cref="INotModifiedResult"/> responses.
    /// </summary>
    /// <param name="serviceProvider">The scoped service provider for the current request.</param>
    /// <param name="context">The operation context for the current pipeline execution.</param>
    /// <param name="handlerResult">The handler result to enrich with links.</param>
    /// <returns>A task that completes when link population has finished (or been skipped).</returns>
    public static Task PopulateHateoasLinksAsync(
        IServiceProvider serviceProvider,
        ApiOperationContext context,
        OperationResult handlerResult)
    {
        // An app that disables links has no link step, as a legacy app without AddHateoasLinks() had none
        // (Blueprint.Http/BlueprintApiBuilderExtensions.cs:101-111 at Signature365 8b40ecc961). A nested pipeline's context
        // carries its parent's app, so a self query's result is not linked either.
        if (handlerResult is INotModifiedResult || context.App.HateoasLinksDisabled)
        {
            return Task.CompletedTask;
        }

        Blueprint.Http.IApiLinkGenerator? apiLinkGenerator = GetRequestLinkGenerator(context);

        if (apiLinkGenerator is null)
        {
            // Fallback: resolve from DI for non-HTTP hosts or test contexts where BaseUri
            // was not pre-computed by the router.
            apiLinkGenerator = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetService<Blueprint.Http.IApiLinkGenerator>(serviceProvider);

            if (apiLinkGenerator == null)
            {
                return Task.CompletedTask;
            }
        }

        return PopulateHateoasLinksCoreAsync(serviceProvider, apiLinkGenerator, context, handlerResult);
    }

    /// <summary>
    /// Core async implementation of HATEOAS link population. Separated from the null-check
    /// fast paths to avoid state machine allocation for not-modified or no-generator cases.
    /// </summary>
    private static async Task PopulateHateoasLinksCoreAsync(
        IServiceProvider serviceProvider,
        Blueprint.Http.IApiLinkGenerator apiLinkGenerator,
        ApiOperationContext context,
        OperationResult handlerResult)
    {
        var resourceLinkGenerators = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<IEnumerable<Blueprint.Http.IResourceLinkGenerator>>(serviceProvider);

        await Blueprint.Http.LinkGeneratorHandler.AddLinksAsync(apiLinkGenerator, resourceLinkGenerators, context, handlerResult).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a link generator for the base URI the router set for the request, or <see langword="null"/> when the operation
    /// is not running in a request (<see cref="ApiOperationContext.BaseUri"/> is not set).
    /// </summary>
    /// <param name="context">The operation context, whose app and base URI the generator is for.</param>
    /// <returns>The generator, or <see langword="null"/> outside a request.</returns>
    internal static ApiLinkGenerator? GetRequestLinkGenerator(ApiOperationContext context)
    {
        if (context.BaseUri is null)
        {
            return null;
        }

        // Fast path: reuse the cached generator when the BaseUri reference hasn't changed
        // (the router middleware reuses the same string instance across requests).
        // Eliminates ~40B allocation per request on the link-generation hot path. The cache is process-wide, so it is
        // reused only for the app it was made for: two apps can share a base URI instance (the testing host gives every app
        // the same one), and a generator's self links and operation URLs are its app's.
        var cached = _cachedApiLinkGenerator;

        if (cached is not null && ReferenceEquals(cached.OriginalBaseUri, context.BaseUri) && ReferenceEquals(cached.App, context.App))
        {
            return cached;
        }

        var apiLinkGenerator = new ApiLinkGenerator(context.App, context.BaseUri);
        _cachedApiLinkGenerator = apiLinkGenerator;

        return apiLinkGenerator;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Parameter binding helpers — replace multi-line inline TryParse
    //  blocks in generated pipelines with single-call shared methods.
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a typed route value. The generated pipeline calls this only when the route value is present, and otherwise
    /// leaves the operation's own value, as legacy Blueprint did. Returns <see langword="null"/> on success, or a
    /// <see cref="BindingFailureResult"/> when unparseable.
    /// </summary>
    /// <typeparam name="T">The target value type.</typeparam>
    /// <param name="routeValues">The route value dictionary from the current request.</param>
    /// <param name="name">The route parameter name to extract.</param>
    /// <param name="value">The parsed value on success.</param>
    /// <param name="tryParse">A static delegate that wraps the type-specific TryParse method.</param>
    /// <returns><see langword="null"/> on success; a <see cref="BindingFailureResult"/> on parse failure.</returns>
    public static OperationResult? BindRouteValue<T>(
        Microsoft.AspNetCore.Routing.RouteValueDictionary routeValues,
        string name,
        out T value,
        TryParseFunc<T> tryParse)
    {
        if (routeValues[name] is string s && tryParse(s, out value))
        {
            return null;
        }

        value = default!;
        return new BindingFailureResult($"Invalid route parameter '{name}'");
    }

    /// <summary>
    /// Parses a typed scalar query parameter. When absent, <paramref name="found"/> is <see langword="false"/>
    /// and the caller skips assignment. When present but unparseable, returns a <see cref="BindingFailureResult"/>.
    /// </summary>
    /// <typeparam name="T">The target value type.</typeparam>
    /// <param name="query">The query collection from the current request.</param>
    /// <param name="name">The query parameter name to extract.</param>
    /// <param name="value">The parsed value on success.</param>
    /// <param name="found">Whether the query parameter was present in the request.</param>
    /// <param name="tryParse">A static delegate that wraps the type-specific TryParse method.</param>
    /// <returns><see langword="null"/> on success or absent; a <see cref="BindingFailureResult"/> on parse failure.</returns>
    public static OperationResult? BindQueryValue<T>(
        Microsoft.AspNetCore.Http.IQueryCollection query,
        string name,
        out T value,
        out bool found,
        TryParseFunc<T> tryParse)
    {
        var sv = query[name];
        if (sv.Count > 0)
        {
            found = true;
            if (tryParse(sv[0]!, out value))
            {
                return null;
            }

            value = default!;
            return new BindingFailureResult($"Invalid query parameter '{name}'");
        }

        found = false;
        value = default!;
        return null;
    }

    /// <summary>
    /// Parses a typed array query parameter. When absent, <paramref name="values"/> is <see langword="null"/>.
    /// When any element fails to parse, returns a <see cref="BindingFailureResult"/>.
    /// </summary>
    /// <typeparam name="T">The array element type.</typeparam>
    /// <param name="query">The query collection from the current request.</param>
    /// <param name="name">The query parameter name to extract.</param>
    /// <param name="values">The parsed array on success, or <see langword="null"/> when absent.</param>
    /// <param name="tryParse">A static delegate that wraps the type-specific TryParse method.</param>
    /// <returns><see langword="null"/> on success or absent; a <see cref="BindingFailureResult"/> on parse failure.</returns>
    public static OperationResult? BindQueryArray<T>(
        Microsoft.AspNetCore.Http.IQueryCollection query,
        string name,
        out T[]? values,
        TryParseFunc<T> tryParse)
    {
        var sv = query[name];
        if (sv.Count > 0)
        {
            var arr = new T[sv.Count];
            for (var i = 0; i < sv.Count; i++)
            {
                if (!tryParse(sv[i]!, out arr[i]))
                {
                    values = null;
                    return new BindingFailureResult($"Invalid query parameter '{name}'");
                }
            }

            values = arr;
            return null;
        }

        values = null;
        return null;
    }

    /// <summary>
    /// Parses a typed header value. When absent, <paramref name="found"/> is <see langword="false"/>.
    /// </summary>
    /// <typeparam name="T">The target value type.</typeparam>
    /// <param name="headers">The header dictionary from the current request.</param>
    /// <param name="name">The header name to extract.</param>
    /// <param name="value">The parsed value on success.</param>
    /// <param name="found">Whether the header was present in the request.</param>
    /// <param name="tryParse">A static delegate that wraps the type-specific TryParse method.</param>
    /// <returns><see langword="null"/> on success or absent; a <see cref="BindingFailureResult"/> on parse failure.</returns>
    public static OperationResult? BindHeaderValue<T>(
        Microsoft.AspNetCore.Http.IHeaderDictionary headers,
        string name,
        out T value,
        out bool found,
        TryParseFunc<T> tryParse)
    {
        var sv = headers[name];
        if (sv.Count > 0)
        {
            found = true;
            if (tryParse(sv[0]!, out value))
            {
                return null;
            }

            value = default!;
            return new BindingFailureResult($"Invalid header '{name}'");
        }

        found = false;
        value = default!;
        return null;
    }

    /// <summary>
    /// Parses a typed cookie value. When absent, <paramref name="found"/> is <see langword="false"/>.
    /// </summary>
    /// <typeparam name="T">The target value type.</typeparam>
    /// <param name="cookies">The cookie collection from the current request.</param>
    /// <param name="name">The cookie name to extract.</param>
    /// <param name="value">The parsed value on success.</param>
    /// <param name="found">Whether the cookie was present in the request.</param>
    /// <param name="tryParse">A static delegate that wraps the type-specific TryParse method.</param>
    /// <returns><see langword="null"/> on success or absent; a <see cref="BindingFailureResult"/> on parse failure.</returns>
    public static OperationResult? BindCookieValue<T>(
        Microsoft.AspNetCore.Http.IRequestCookieCollection cookies,
        string name,
        out T value,
        out bool found,
        TryParseFunc<T> tryParse)
    {
        if (cookies.TryGetValue(name, out var cookieStr))
        {
            found = true;
            if (tryParse(cookieStr, out value))
            {
                return null;
            }

            value = default!;
            return new BindingFailureResult($"Invalid cookie '{name}'");
        }

        found = false;
        value = default!;
        return null;
    }

    private static readonly ConcurrentDictionary<(System.Text.Json.Serialization.JsonSerializerContext?, Type), System.Text.Json.Serialization.Metadata.JsonTypeInfo?> _inlineTypeInfoCache = new();

    // Thread-local buffer and writer for inline JSON response writing from generated pipelines.
    // These are the same pattern as HttpOperationResultExecutor's fields — thread locality eliminates contention, and a
    // write that does not finish at once takes the buffer from the thread.
    [ThreadStatic]
    private static System.Buffers.ArrayBufferWriter<byte>? _inlineResponseBuffer;

    [ThreadStatic]
    private static System.Text.Json.Utf8JsonWriter? _inlineResponseWriter;

    /// <summary>
    /// Writes a 200 OK JSON response directly from the generated pipeline, bypassing
    /// <see cref="OkResult{T}"/> allocation. The caller should return
    /// <see cref="Http.HttpResponseAlreadyWrittenResult.Instance"/> after calling this method.
    /// </summary>
    /// <remarks>
    /// This method replicates the fast-path logic from <c>HttpOperationResultExecutor.HandleOkResult</c>
    /// but operates on a typed content reference, eliminating the need for an intermediate heap-allocated
    /// <see cref="OkResult{T}"/> wrapper (~40 bytes per request).
    /// </remarks>
    /// <param name="httpContext">The HTTP context for the current request.</param>
    /// <param name="operationContext">The operation context providing access to the app serializer.</param>
    /// <param name="content">The typed response content to serialize as JSON.</param>
    /// <returns>A task that completes when the response body has been written.</returns>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Fallback serialization path mirrors HttpOperationResultExecutor — only reached when no app-owned JsonSerializerContext covers the response type.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "Fallback serialization path mirrors HttpOperationResultExecutor — AOT consumers must ensure their JsonSerializerContext covers all response types.")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task WriteOkJsonResponseAsync(
        Microsoft.AspNetCore.Http.HttpContext httpContext,
        ApiOperationContext operationContext,
        object content)
    {
        httpContext.Response.StatusCode = 200;
        httpContext.Response.ContentType = "application/json; charset=utf-8";

        var serializerContext = operationContext.App.AppOwnedSerializerContext;
        var contentType = content.GetType();

        var jsonTypeInfo = _inlineTypeInfoCache.GetOrAdd(
            (serializerContext, contentType),
            static key => key.Item1?.GetTypeInfo(key.Item2));

        if (jsonTypeInfo is not null)
        {
            var buffer = _inlineResponseBuffer ??= new System.Buffers.ArrayBufferWriter<byte>(512);
            buffer.Clear();

            var writer = _inlineResponseWriter;
            if (writer is null)
            {
                writer = new System.Text.Json.Utf8JsonWriter(buffer);
                _inlineResponseWriter = writer;
            }
            else
            {
                writer.Reset(buffer);
            }

            System.Text.Json.JsonSerializer.Serialize(writer, content, jsonTypeInfo);
            writer.Flush();

            httpContext.Response.ContentLength = buffer.WrittenCount;

            var writeTask = httpContext.Response.Body.WriteAsync(buffer.WrittenMemory, httpContext.RequestAborted);

            // A write that has not finished may still read the buffer after an await (a compressing body stream does),
            // while this thread serves another request: that write takes the buffer, and the thread's next one gets a
            // new buffer, so one response can never carry another's bytes (HttpOperationResultExecutor.WriteThreadBufferAsync).
            if (!writeTask.IsCompleted)
            {
                _inlineResponseBuffer = null;
            }

            return writeTask.IsCompletedSuccessfully
                ? Task.CompletedTask
                : writeTask.AsTask();
        }
        else
        {
            return System.Text.Json.JsonSerializer.SerializeAsync(
                httpContext.Response.Body, content, contentType, serializerContext?.Options, httpContext.RequestAborted);
        }
    }

    /// <summary>
    /// A stack-allocated <see cref="IReadOnlyList{T}"/> that holds exactly one element,
    /// avoiding the heap allocation of a single-element <c>T[]</c> array. Generated pipelines
    /// use this for the common case of operations with exactly one <c>[Resource]</c> attribute
    /// (the vast majority of S365 operations).
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    public readonly struct SingleItemList<T> : IReadOnlyList<T>
    {
        private readonly T _item;

        /// <summary>
        /// Initialises a new <see cref="SingleItemList{T}"/> containing <paramref name="item"/>.
        /// </summary>
        /// <param name="item">The single element to hold.</param>
        public SingleItemList(T item)
        {
            this._item = item;
        }

        /// <inheritdoc />
        public int Count => 1;

        /// <inheritdoc />
        public T this[int index] => index == 0
            ? this._item
            : throw new ArgumentOutOfRangeException(nameof(index));

        /// <summary>
        /// Returns a struct enumerator that avoids the heap allocation of the <c>yield return</c>
        /// state machine generated by the compiler.
        /// </summary>
        /// <returns>A <see cref="Enumerator"/> over the single element.</returns>
        public Enumerator GetEnumerator()
        {
            return new(this._item);
        }

        /// <inheritdoc />
        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return new Enumerator(this._item);
        }

        /// <inheritdoc />
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return new Enumerator(this._item);
        }

        /// <summary>
        /// Zero-allocation enumerator for <see cref="SingleItemList{T}"/>.
        /// </summary>
        public struct Enumerator : IEnumerator<T>
        {
            private readonly T _item;
            private bool _moved;

            /// <summary>
            /// Initialises a new enumerator over the single <paramref name="item"/>.
            /// </summary>
            /// <param name="item">The single element to enumerate.</param>
            internal Enumerator(T item)
            {
                this._item = item;
                this._moved = false;
            }

            /// <inheritdoc />
            public readonly T Current => this._item;

            /// <inheritdoc />
            readonly object? System.Collections.IEnumerator.Current => this._item;

            /// <inheritdoc />
            public bool MoveNext()
            {
                if (!this._moved)
                {
                    this._moved = true;
                    return true;
                }

                return false;
            }

            /// <inheritdoc />
            public void Reset()
            {
                this._moved = false;
            }

            /// <inheritdoc />
            public readonly void Dispose()
            {
            }
        }
    }
}

/// <summary>
/// Delegate used by <see cref="PipelineHelpers"/> binding methods to perform type-specific
/// string-to-<typeparamref name="T"/> conversion without reflection. Generated pipelines
/// pass static lambdas wrapping the appropriate <c>TryParse</c> call for each property type.
/// </summary>
/// <typeparam name="T">The target type.</typeparam>
/// <param name="input">The raw string value to parse.</param>
/// <param name="result">When <see langword="true"/> is returned, contains the parsed value.</param>
/// <returns><see langword="true"/> when parsing succeeds.</returns>
public delegate bool TryParseFunc<T>(string input, out T result);

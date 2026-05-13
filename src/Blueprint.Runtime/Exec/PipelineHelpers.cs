using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
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
    private static readonly ConcurrentDictionary<string, ActivityTagsCollection> _auditTagsCache = new();

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
        var ivoContext = new System.ComponentModel.DataAnnotations.ValidationContext(operation);
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
    /// Invokes registered <see cref="IPropertyPopulator"/> instances in order, using an optimised
    /// single-populator fast path to avoid enumerator allocation when only one is registered.
    /// </summary>
    /// <param name="populators">The pre-resolved array of property populators (empty when none are registered).</param>
    /// <param name="context">The operation context that the populators will enrich.</param>
    /// <returns>A task that completes when all populators have run.</returns>
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
    public delegate void PopulateFromBodyDelegate<in TOperation>(ReadOnlySpan<byte> bodyBytes, TOperation operation);

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
    /// <returns>
    /// <c>null</c> when the body was read and populated successfully (or was empty/absent),
    /// or an <see cref="OperationResult"/> representing the failure (unsupported media type,
    /// invalid JSON, etc.).
    /// </returns>
    public static async Task<OperationResult?> ReadJsonBodyAsync<TOperation>(
        HttpContext httpContext,
        CancellationToken cancellationToken,
        TOperation operation,
        PopulateFromBodyDelegate<TOperation> populate)
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
            return UnsupportedMediaTypeResult.Instance;
        }

        // Fast path: Content-Length is known — rent a pooled buffer and read directly
        if (httpContext.Request.ContentLength.HasValue && httpContext.Request.ContentLength.Value > 0)
        {
            var bodyLength = (int)httpContext.Request.ContentLength.Value;
            var rentedBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(bodyLength);

            try
            {
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
                        populate(new ReadOnlySpan<byte>(rentedBuffer, 0, totalRead), operation);
                    }
                    catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or NotSupportedException)
                    {
                        return new BindingFailureResult("Invalid JSON body");
                    }
                }

                return null;
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }

        // Slow path: chunked transfer or unknown Content-Length — use MemoryStream
        using var bodyStream = new MemoryStream();
        await httpContext.Request.Body.CopyToAsync(bodyStream, cancellationToken).ConfigureAwait(false);

        if (bodyStream.Length > 0)
        {
            try
            {
                populate(new ReadOnlySpan<byte>(bodyStream.GetBuffer(), 0, (int)bodyStream.Length), operation);
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or NotSupportedException)
            {
                return new BindingFailureResult("Invalid JSON body");
            }
        }

        return null;
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
    public static async Task LoadUserContextAsync(
        ApiOperationContext context,
        HttpContext httpContext,
        Authorization.IUserAuthorisationContextFactory? userContextFactory)
    {
        if (userContextFactory != null)
        {
            if (httpContext.User.Identity is System.Security.Claims.ClaimsIdentity { IsAuthenticated: true } userIdentity)
            {
                context.UserAuthorisationContext = await userContextFactory
                    .CreateContextAsync(userIdentity, context.OperationCancelled)
                    .ConfigureAwait(false);
            }
        }

        if (context.UserAuthorisationContext != null && context.Activity != null)
        {
            var activity = context.Activity;
            activity.SetTag("enduser.id", context.UserAuthorisationContext.Id);
            activity.SetTag("enduser.account_id", context.UserAuthorisationContext.AccountId);
            context.UserAuthorisationContext.PopulateMetadata((key, value) => activity.SetTag(key, value));
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
        tenantAccessor.Current = userPrincipal is not null
            ? tenantResolver.ResolveTenantId(userPrincipal)
            : null;
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

        return scheduler != null
            ? scheduler.RunNowAsync().AsTask()
            : Task.CompletedTask;
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
    public static async Task<OperationResult?> RunAuthorisersAsync(
        ApiOperationContext context,
        Authorization.IApiAuthoriser[] apiAuthorisers,
        Authorization.IOperationAuthoriser[] operationAuthorisers)
    {
        if (context.SkipAuthorisation)
        {
            return null;
        }

        for (var i = 0; i < apiAuthorisers.Length; i++)
        {
            var authoriser = apiAuthorisers[i];

            if (authoriser.AppliesTo(context.Descriptor))
            {
                var result = await authoriser.CanExecuteOperationAsync(context, context.Descriptor, context.Operation)
                    .ConfigureAwait(false);

                if (!result.IsAllowed)
                {
                    return result.FailureType == Authorization.ExecutionAllowedFailureType.Authentication
                        ? UnauthenticatedResult.Instance
                        : ForbiddenResult.Instance;
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
                    return result.FailureType == Authorization.ExecutionAllowedFailureType.Authentication
                        ? UnauthenticatedResult.Instance
                        : ForbiddenResult.Instance;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Checks resource authorization using the compile-time-expanded
    /// <see cref="Authorization.IResolvedResourceAuthorizationProvider"/>. Returns
    /// <see cref="ForbiddenResult.Instance"/> when the provider denies access, or <c>null</c>
    /// when the caller is authorized or the provider is not registered (permissive default).
    /// </summary>
    /// <remarks>
    /// Generated pipelines call this overload inside the <c>if (_resolvedResourceAuthorizationProvider != null)</c>
    /// branch so resolved scope-pair construction (which contains compile-time expanded expressions)
    /// is lazy and only runs when a resolved provider is actually registered.
    /// </remarks>
    /// <param name="context">The current operation execution context.</param>
    /// <param name="principal">The authenticated caller's claims principal.</param>
    /// <param name="resolvedProvider">The resolved resource authorization provider, or <c>null</c> if none is registered.</param>
    /// <param name="resolvedScopes">The compile-time expanded scope pairs for this operation.</param>
    /// <param name="cancellationToken">Cancellation token for the authorization evaluation.</param>
    /// <returns><c>null</c> when authorized; <see cref="ForbiddenResult.Instance"/> when denied.</returns>
    public static async Task<OperationResult?> CheckResourceAuthorizationAsync(
        ApiOperationContext context,
        ClaimsPrincipal principal,
        Authorization.IResolvedResourceAuthorizationProvider? resolvedProvider,
        IReadOnlyList<ResolvedScopePair> resolvedScopes,
        CancellationToken cancellationToken)
    {
        if (resolvedProvider == null)
        {
            return null;
        }

        var isAuthorized = await resolvedProvider.IsAuthorizedAsync(context, principal, resolvedScopes, cancellationToken).ConfigureAwait(false);

        return isAuthorized ? null : ForbiddenResult.Instance;
    }

    /// <summary>
    /// Single-element overload for <see cref="CheckResourceAuthorizationAsync(ApiOperationContext, ClaimsPrincipal, Authorization.IResolvedResourceAuthorizationProvider, IReadOnlyList{ResolvedScopePair}, CancellationToken)"/>.
    /// Avoids per-request array allocation for the common case of operations with exactly one
    /// <c>[Resource]</c> attribute. The scope pair is passed by value and wrapped in a stack-allocated
    /// <see cref="SingleItemList{T}"/> that implements <see cref="IReadOnlyList{T}"/>.
    /// </summary>
    /// <param name="context">The current operation execution context.</param>
    /// <param name="principal">The authenticated caller's claims principal.</param>
    /// <param name="resolvedProvider">The resolved resource authorization provider, or <c>null</c> if none is registered.</param>
    /// <param name="singleScope">The single compile-time expanded scope pair for this operation.</param>
    /// <param name="cancellationToken">Cancellation token for the authorization evaluation.</param>
    /// <returns><c>null</c> when authorized; <see cref="ForbiddenResult.Instance"/> when denied.</returns>
    public static async Task<OperationResult?> CheckResourceAuthorizationAsync(
        ApiOperationContext context,
        ClaimsPrincipal principal,
        Authorization.IResolvedResourceAuthorizationProvider? resolvedProvider,
        ResolvedScopePair singleScope,
        CancellationToken cancellationToken)
    {
        if (resolvedProvider == null)
        {
            return null;
        }

        var scopeList = new SingleItemList<ResolvedScopePair>(singleScope);
        var isAuthorized = await resolvedProvider.IsAuthorizedAsync(context, principal, scopeList, cancellationToken).ConfigureAwait(false);

        return isAuthorized ? null : ForbiddenResult.Instance;
    }

    /// <summary>
    /// Checks resource authorization using the legacy <see cref="Authorization.IResourceAuthorizationProvider"/>
    /// with raw template strings. Returns <see cref="ForbiddenResult.Instance"/> when the provider
    /// denies access, or <c>null</c> when the caller is authorized or the provider is not registered
    /// (permissive default).
    /// </summary>
    /// <param name="context">The current operation execution context.</param>
    /// <param name="principal">The authenticated caller's claims principal.</param>
    /// <param name="legacyProvider">The resource authorization provider, or <c>null</c> if none is registered.</param>
    /// <param name="legacyScopes">The static scope pairs for this operation (raw template strings).</param>
    /// <param name="cancellationToken">Cancellation token for the authorization evaluation.</param>
    /// <returns><c>null</c> when authorized; <see cref="ForbiddenResult.Instance"/> when denied.</returns>
    public static async Task<OperationResult?> CheckResourceAuthorizationAsync(
        ApiOperationContext context,
        ClaimsPrincipal principal,
        Authorization.IResourceAuthorizationProvider? legacyProvider,
        IReadOnlyList<ScopePair> legacyScopes,
        CancellationToken cancellationToken)
    {
        if (legacyProvider == null)
        {
            return null;
        }

        var isAuthorized = await legacyProvider.IsAuthorizedAsync(context, principal, legacyScopes, cancellationToken).ConfigureAwait(false);

        return isAuthorized ? null : ForbiddenResult.Instance;
    }

    /// <summary>
    /// Executes the resource-event audit sink with best-effort semantics when the handler result
    /// is a <see cref="ResourceEventResult"/> or <see cref="KeyOnlyResourceEventResult"/>.
    /// Extracts the event/resource identity from the result, records started/succeeded/failed
    /// activity events, and swallows sink exceptions. When the sink is <c>null</c> or the result
    /// is not a resource-event type, the method returns immediately with no overhead.
    /// </summary>
    /// <param name="auditSink">The resource-event audit sink, or <c>null</c> if none is registered.</param>
    /// <param name="context">The operation context for the current pipeline execution.</param>
    /// <param name="handlerResult">The handler result — only processed if it is a resource-event result type.</param>
    /// <param name="operationTypeName">The operation type name for activity event tags.</param>
    /// <returns>A task that completes when the audit sink has finished (or been skipped).</returns>
    public static Task ExecuteResourceEventAuditSinkAsync(
        IResourceEventAuditSink? auditSink,
        ApiOperationContext context,
        OperationResult handlerResult,
        string operationTypeName)
    {
        return auditSink != null
            ? ExecuteResourceEventAuditSinkCoreAsync(auditSink, context, handlerResult, operationTypeName)
            : Task.CompletedTask;
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

    /// <summary>
    /// Resolves <c>IApiLinkGenerator</c> and <c>IResourceLinkGenerator</c> from DI and invokes
    /// <see cref="Blueprint.Http.LinkGeneratorHandler"/> to populate all HATEOAS links on the
    /// returned resource. Skips link population entirely for <see cref="INotModifiedResult"/>
    /// responses or when link generation is not registered. Replaces the 10-line inline block
    /// that was previously emitted per-pipeline.
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
        if (handlerResult is INotModifiedResult)
        {
            return Task.CompletedTask;
        }

        var apiLinkGenerator = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetService<Blueprint.Http.IApiLinkGenerator>(serviceProvider);

        if (apiLinkGenerator == null)
        {
            return Task.CompletedTask;
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

    // ────────────────────────────────────────────────────────────────────
    //  Parameter binding helpers — replace multi-line inline TryParse
    //  blocks in generated pipelines with single-call shared methods.
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a typed route value. Route parameters are always present (enforced by routing).
    /// Returns <see langword="null"/> on success, or a <see cref="BindingFailureResult"/> when unparseable.
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

        /// <inheritdoc />
        public IEnumerator<T> GetEnumerator()
        {
            yield return this._item;
        }

        /// <inheritdoc />
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
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

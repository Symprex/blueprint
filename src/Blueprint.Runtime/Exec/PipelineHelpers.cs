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
    public static async Task FlushBackgroundTasksAsync(IServiceProvider serviceProvider)
    {
        var scheduler = serviceProvider.GetService<Blueprint.Tasks.IBackgroundTaskScheduler>();

        if (scheduler != null)
        {
            await scheduler.RunNowAsync().ConfigureAwait(false);
        }
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
    public static async Task ExecuteResourceEventAuditSinkAsync(
        IResourceEventAuditSink? auditSink,
        ApiOperationContext context,
        OperationResult handlerResult,
        string operationTypeName)
    {
        if (auditSink == null)
        {
            return;
        }

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
    public static async Task PopulateHateoasLinksAsync(
        IServiceProvider serviceProvider,
        ApiOperationContext context,
        OperationResult handlerResult)
    {
        if (handlerResult is INotModifiedResult)
        {
            return;
        }

        var apiLinkGenerator = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetService<Blueprint.Http.IApiLinkGenerator>(serviceProvider);

        if (apiLinkGenerator == null)
        {
            return;
        }

        var resourceLinkGenerators = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<IEnumerable<Blueprint.Http.IResourceLinkGenerator>>(serviceProvider);

        await Blueprint.Http.LinkGeneratorHandler.AddLinksAsync(apiLinkGenerator, resourceLinkGenerators, context, handlerResult).ConfigureAwait(false);
    }
}

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Blueprint;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Runtime.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

// This is the recommendation from MS for extensions to IServiceCollection to aid discoverability
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Adds Blueprint service registration helpers to ASP.NET Core's service collection for discoverable application startup.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Blueprint application and executor types needed to execute operations through the generated pipeline.
    /// </summary>
    /// <typeparam name="TApp">The Blueprint app type (must extend <see cref="BlueprintApp"/>).</typeparam>
    /// <typeparam name="TExec">The generated executor type.</typeparam>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="configure">
    /// An optional delegate to configure additional Blueprint services such as
    /// <see cref="Blueprint.Auditing.IAuditSink"/>, <see cref="Blueprint.Authorization.IResourceAuthorizationProvider"/>,
    /// or custom <see cref="HttpOperationResultExecutor"/> overrides.
    /// </param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddBlueprintApi<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TExec>(
        this IServiceCollection services,
        Action<BlueprintServiceBuilder>? configure = null)
        where TApp : BlueprintApp, new()
        where TExec : BlueprintAppExecutor
    {
        // Created first so the app's factory reads what configure (run below, before the app is ever resolved) sets on it.
        var builder = new BlueprintServiceBuilder(services);

        // The app's serializer context is installed on the shared BlueprintJsonOptions as they are made, whichever of the
        // options and the app is asked for first: a client that shares the options may serialise, which makes them
        // read-only, before the app is ever resolved.
        services.AddSingleton(sp => new BlueprintAppSerializerContextInstaller<TApp>(
            sp,
            () => new TApp { HateoasLinksDisabled = builder.HateoasLinksDisabled }));
        services.AddSingleton<IPostConfigureOptions<BlueprintJsonOptions>>(static sp => sp.GetRequiredService<BlueprintAppSerializerContextInstaller<TApp>>());
        services.AddSingleton<TApp>(static sp => sp.GetRequiredService<BlueprintAppSerializerContextInstaller<TApp>>().ResolveApp());
        services.AddSingleton<BlueprintApp>(static sp => sp.GetRequiredService<TApp>());
        services.AddSingleton<TExec>();

        // Register the base executor type so handlers and infrastructure can inject
        // BlueprintAppExecutor for nested operation execution without knowing the concrete type.
        services.AddSingleton<BlueprintAppExecutor>(sp => sp.GetRequiredService<TExec>());

        // Default link generation infrastructure.
        // ApiLinkGenerator needs the per-request base URI from ApiOperationContext,
        // which is set by BlueprintApiRouter before pipeline execution.
        // IHttpContextAccessor bridges the gap: the scoped factory reads the current
        // request's scheme/host/pathBase to construct the base URI dynamically.
        // The base URI is cached per scheme+host+pathBase combination since it is
        // almost always identical across requests in a given deployment.
        services.TryAddSingleton<Microsoft.AspNetCore.Http.IHttpContextAccessor, Microsoft.AspNetCore.Http.HttpContextAccessor>();
        var baseUriCache = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        services.TryAddScoped<Blueprint.Http.IApiLinkGenerator>(sp =>
        {
            var app = sp.GetRequiredService<BlueprintApp>();
            var httpContextAccessor = sp.GetService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
            var httpContext = httpContextAccessor?.HttpContext;

            string baseUri;
            if (httpContext is not null)
            {
                var request = httpContext.Request;
                var host = request.Host.Value ?? string.Empty;
                var pathBase = request.PathBase.HasValue ? request.PathBase.Value! : string.Empty;
                var cacheKey = string.Concat(request.Scheme, "://", host, pathBase, "/");
                baseUri = baseUriCache.GetOrAdd(cacheKey, static k => k);
            }
            else
            {
                baseUri = "https://localhost/";
            }

            return new Blueprint.Http.ApiLinkGenerator(app, baseUri);
        });
        services.TryAddScoped<Blueprint.Http.IResourceLinkGenerator, Blueprint.Http.EntityOperationResourceLinkGenerator>();

        // Authorization infrastructure — always registered so that link generation (which depends
        // on IApiAuthoriserAggregator for link-visibility filtering) works even when no explicit
        // authorisers are configured. Scoped because IApiAuthoriser implementations may have
        // scoped dependencies (e.g. per-request claim inspectors).
        services.TryAddScoped<Blueprint.Authorization.IApiAuthoriserAggregator, Blueprint.Authorization.ApiAuthoriserAggregator>();

        // Pre-rendered OpenAPI document holder — populated at endpoint mapping time
        services.TryAddSingleton<PreRenderedOpenApiDocumentHolder>();

        configure?.Invoke(builder);

        return services;
    }
}

/// <summary>
/// Owns an app's construction and installs its serializer context on the <see cref="BlueprintJsonOptions"/> when they are
/// made, so that the context does not depend on whether the app or the options are resolved first.
/// </summary>
/// <remarks>
/// The options never resolve the app through the service provider (which would wait on the app's own resolution while the
/// app's resolution waits on the options); both take the one instance from here.
/// </remarks>
/// <typeparam name="TApp">The app whose serializer context is installed.</typeparam>
internal sealed class BlueprintAppSerializerContextInstaller<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp> : IPostConfigureOptions<BlueprintJsonOptions>
    where TApp : BlueprintApp
{
    private readonly IServiceProvider _services;
    private readonly Lazy<TApp> _app;
    private int _installed;

    /// <summary>
    /// Initialises a new instance of the <see cref="BlueprintAppSerializerContextInstaller{TApp}"/> class.
    /// </summary>
    /// <param name="services">The service provider the options are resolved from.</param>
    /// <param name="createApp">Creates the app, called once.</param>
    public BlueprintAppSerializerContextInstaller(IServiceProvider services, Func<TApp> createApp)
    {
        this._services = services;
        this._app = new Lazy<TApp>(createApp);
    }

    /// <summary>
    /// Returns the app, having made the options first (unless something is making them already) so that its context is
    /// installed whenever the app is resolved.
    /// </summary>
    /// <returns>The app.</returns>
    public TApp ResolveApp()
    {
        var app = this._app.Value;

        _ = this._services.GetService<IOptions<BlueprintJsonOptions>>()?.Value;

        return app;
    }

    /// <inheritdoc />
    public void PostConfigure(string? name, BlueprintJsonOptions options)
    {
        // Only once, and only for the options the app's host and its clients share: a named or snapshot copy is not theirs.
        if (!string.IsNullOrEmpty(name) || Interlocked.Exchange(ref this._installed, 1) != 0)
        {
            return;
        }

        this._app.Value.ApplySerializerOptions(options.SerializerOptions);
    }
}

/// <summary>
/// Provides a fluent interface for registering additional Blueprint services during application startup.
/// </summary>
public sealed class BlueprintServiceBuilder
{
    /// <summary>
    /// Gets the underlying service collection for registering additional services.
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Initialises a new instance of the <see cref="BlueprintServiceBuilder"/> class wrapping the given service collection.
    /// </summary>
    /// <param name="services">The underlying service collection.</param>
    internal BlueprintServiceBuilder(IServiceCollection services)
    {
        this.Services = services;
    }

    /// <summary>
    /// Registers a general-purpose audit sink that records completed operation executions.
    /// </summary>
    /// <typeparam name="T">The <see cref="Blueprint.Auditing.IAuditSink"/> implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder AddAuditSink<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, Blueprint.Auditing.IAuditSink
    {
        this.Services.AddSingleton<Blueprint.Auditing.IAuditSink, T>();
        return this;
    }

    /// <summary>
    /// Registers a resource-event audit sink that records domain events after successful handler execution.
    /// </summary>
    /// <typeparam name="T">The <see cref="Blueprint.Auditing.IResourceEventAuditSink"/> implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder AddResourceEventAuditSink<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, Blueprint.Auditing.IResourceEventAuditSink
    {
        this.Services.AddSingleton<Blueprint.Auditing.IResourceEventAuditSink, T>();
        return this;
    }

    /// <summary>
    /// Registers a resource authorization provider that evaluates scope-pair permissions at pipeline execution time.
    /// </summary>
    /// <typeparam name="T">The <see cref="Blueprint.Authorization.IResourceAuthorizationProvider"/> implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder AddResourceAuthorizationProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, Blueprint.Authorization.IResourceAuthorizationProvider
    {
        this.Services.AddSingleton<Blueprint.Authorization.IResourceAuthorizationProvider, T>();

        return this;
    }

    /// <summary>
    /// Registers a pre-configured <see cref="Blueprint.Authorization.IResourceAuthorizationProvider"/> instance.
    /// Use this overload with factory methods such as
    /// <see cref="Blueprint.Authorization.ScopePairClaimAuthorizationProvider">ScopePairClaimAuthorizationProvider.WithCrudVerbHierarchy</see>.
    /// </summary>
    /// <param name="instance">The authorization provider instance to register as a singleton.</param>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder AddResourceAuthorizationProvider(
        Blueprint.Authorization.IResourceAuthorizationProvider instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        this.Services.AddSingleton(instance);

        return this;
    }

    /// <summary>
    /// Registers a custom HTTP result executor that translates operation results to HTTP responses.
    /// </summary>
    /// <typeparam name="T">The <see cref="HttpOperationResultExecutor"/> implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder AddHttpResultExecutor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : HttpOperationResultExecutor
    {
        this.Services.AddSingleton<HttpOperationResultExecutor, T>();
        return this;
    }

    /// <summary>
    /// Registers a background task scheduler that holds enqueued tasks until pipeline flush.
    /// The scheduler is registered as scoped so each operation gets its own task queue.
    /// </summary>
    /// <typeparam name="T">The <see cref="Blueprint.Tasks.IBackgroundTaskScheduler"/> implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder AddBackgroundTaskScheduler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, Blueprint.Tasks.IBackgroundTaskScheduler
    {
        this.Services.AddScoped<Blueprint.Tasks.IBackgroundTaskScheduler, T>();
        return this;
    }

    /// <summary>
    /// Registers a background task schedule provider that performs the actual dispatch of tasks
    /// to infrastructure such as Hangfire, Azure Queue Storage, or an in-process dispatcher.
    /// Also registers the production <see cref="Blueprint.Runtime.Tasks.BackgroundTaskScheduler"/>
    /// (with trace-context preprocessing) if no custom scheduler has been registered.
    /// </summary>
    /// <typeparam name="T">The <see cref="Blueprint.Tasks.IBackgroundTaskScheduleProvider"/> implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder AddBackgroundTaskScheduleProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, Blueprint.Tasks.IBackgroundTaskScheduleProvider
    {
        this.Services.AddSingleton<Blueprint.Tasks.IBackgroundTaskScheduleProvider, T>();
        this.Services.TryAddScoped<Blueprint.Tasks.IBackgroundTaskScheduler, Blueprint.Runtime.Tasks.BackgroundTaskScheduler>();
        this.Services.TryAddSingleton<Blueprint.Runtime.Tasks.TaskExecutor>();

        // Register both preprocessors. Using Add (not TryAdd) because IEnumerable<IBackgroundTaskPreprocessor>
        // resolves all registrations — trace context and ambient context capture should both run.
        this.Services.TryAddEnumerable(ServiceDescriptor.Singleton<Blueprint.Tasks.IBackgroundTaskPreprocessor, Blueprint.Runtime.Tasks.TraceContextPreprocessor>());
        this.Services.TryAddEnumerable(ServiceDescriptor.Singleton<Blueprint.Tasks.IBackgroundTaskPreprocessor, Blueprint.Runtime.Tasks.ContextCapturingPreprocessor>());
        return this;
    }

    /// <summary>
    /// Registers a background task context provider that captures ambient context (e.g., tenant ID,
    /// user ID, correlation ID) when tasks are scheduled and restores it when they execute.
    /// Multiple providers are supported and all will be called during capture and restore.
    /// </summary>
    /// <typeparam name="T">The <see cref="Blueprint.Tasks.IBackgroundTaskContextProvider"/> implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder AddBackgroundTaskContextProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, Blueprint.Tasks.IBackgroundTaskContextProvider
    {
        this.Services.AddSingleton<Blueprint.Tasks.IBackgroundTaskContextProvider, T>();
        return this;
    }

    /// <summary>
    /// Registers a unit-of-work implementation for persistence coordination.
    /// The UoW is registered as scoped so each operation tracks its own changes.
    /// </summary>
    /// <typeparam name="T">The <see cref="Blueprint.Persistence.IUnitOfWork"/> implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder AddUnitOfWork<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, Blueprint.Persistence.IUnitOfWork
    {
        this.Services.AddScoped<T>();
        this.Services.AddScoped<Blueprint.Persistence.IUnitOfWork>(static sp => sp.GetRequiredService<T>());
        return this;
    }

    /// <summary>
    /// Registers a message outbox implementation for transactional message publication.
    /// The outbox is registered as scoped so each operation gets its own outbox instance.
    /// Messages enqueued during handler execution are committed only on success.
    /// <para>
    /// This is the compile-time equivalent of legacy Blueprint's
    /// <c>StartRebusTransactionScopeMiddleware</c> / <c>CommitRebusTransactionScopeMiddleware</c>
    /// pattern. In production, the implementation typically wraps a messaging library's
    /// transaction scope (e.g., Rebus's <c>RebusTransactionScope</c>).
    /// </para>
    /// </summary>
    /// <typeparam name="T">The <see cref="Blueprint.Messaging.IMessageOutbox"/> implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder AddMessageOutbox<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, Blueprint.Messaging.IMessageOutbox
    {
        this.Services.AddScoped<T>();
        this.Services.AddScoped<Blueprint.Messaging.IMessageOutbox>(static sp => sp.GetRequiredService<T>());
        return this;
    }

    /// <summary>
    /// Registers a property populator that auto-fills operation properties from ambient context
    /// (e.g., JWT claims, tenant accessor, or correlation state) during the Population pipeline stage.
    /// <para>
    /// Multiple populators are supported and will be called in registration order. This is the
    /// compile-time equivalent of legacy Blueprint's <c>SetTenantMiddleware</c> pattern.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The <see cref="Blueprint.Runtime.Middleware.IPropertyPopulator"/> implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder AddPropertyPopulator<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, Blueprint.Runtime.Middleware.IPropertyPopulator
    {
        this.Services.AddSingleton<Blueprint.Runtime.Middleware.IPropertyPopulator, T>();
        return this;
    }

    /// <summary>
    /// Registers a factory that converts a <see cref="System.Security.Claims.ClaimsIdentity"/> into an
    /// application-specific <see cref="Blueprint.Authorization.IUserAuthorisationContext"/>.
    /// The generated pipeline resolves this (optionally) during the Authentication stage to populate
    /// <see cref="Blueprint.Runtime.Exec.ApiOperationContext.UserAuthorisationContext"/>.
    /// </summary>
    /// <typeparam name="T">The <see cref="Blueprint.Authorization.IUserAuthorisationContextFactory"/> implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder AddUserAuthorisationContextFactory<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, Blueprint.Authorization.IUserAuthorisationContextFactory
    {
        this.Services.AddSingleton<Blueprint.Authorization.IUserAuthorisationContextFactory, T>();
        return this;
    }

    /// <summary>
    /// Registers an API authoriser that performs business-level authorization checks
    /// (e.g., tenant isolation, feature-flag gating, or custom permission logic) during the
    /// Authorisation pipeline stage. Multiple authorisers are supported and evaluated in order.
    /// Use this for authorisers that participate in both execution and link-visibility checks.
    /// </summary>
    /// <typeparam name="T">The <see cref="Blueprint.Authorization.IApiAuthoriser"/> implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder AddApiAuthoriser<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, Blueprint.Authorization.IApiAuthoriser
    {
        this.Services.AddSingleton<Blueprint.Authorization.IApiAuthoriser, T>();

        // Ensure the aggregator is registered (idempotent via TryAdd)
        this.Services.TryAddScoped<Blueprint.Authorization.IApiAuthoriserAggregator, Blueprint.Authorization.ApiAuthoriserAggregator>();

        return this;
    }

    /// <summary>
    /// Registers an execution-only authoriser that checks whether the current user can execute
    /// a given operation. The authoriser does not participate in link-visibility decisions.
    /// </summary>
    /// <typeparam name="T">The <see cref="Blueprint.Authorization.IOperationAuthoriser"/> implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder AddOperationAuthoriser<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, Blueprint.Authorization.IOperationAuthoriser
    {
        this.Services.AddSingleton<Blueprint.Authorization.IOperationAuthoriser, T>();

        return this;
    }

    /// <summary>
    /// Registers a link-visibility authoriser that controls whether operation links appear in
    /// API responses. <see cref="Blueprint.Authorization.StateBasedLinkChecker{TOperation,TResource}"/>
    /// is the recommended base class for state-based visibility checks.
    /// </summary>
    /// <typeparam name="T">The <see cref="Blueprint.Authorization.ILinkVisibilityAuthoriser"/> implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder AddLinkVisibilityAuthoriser<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, Blueprint.Authorization.ILinkVisibilityAuthoriser
    {
        this.Services.AddSingleton<Blueprint.Authorization.ILinkVisibilityAuthoriser, T>();

        // Ensure the aggregator is registered (idempotent via TryAdd)
        this.Services.TryAddScoped<Blueprint.Authorization.IApiAuthoriserAggregator, Blueprint.Authorization.ApiAuthoriserAggregator>();

        return this;
    }

    /// <summary>
    /// Registers a context metadata provider that enriches resource events and audit records
    /// with ambient context data (tenant ID, correlation ID, user info, etc.).
    /// Multiple providers are supported and each will be called to contribute metadata.
    /// </summary>
    /// <typeparam name="T">The <see cref="Blueprint.Model.IContextMetadataProvider"/> implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder AddContextMetadataProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, Blueprint.Model.IContextMetadataProvider
    {
        this.Services.TryAddEnumerable(ServiceDescriptor.Scoped<Blueprint.Model.IContextMetadataProvider, T>());
        return this;
    }

    /// <summary>
    /// Registers a resource link generator that dynamically adds links to resources at runtime.
    /// Multiple generators are supported and all will be invoked on matching resource types.
    /// </summary>
    /// <typeparam name="T">The <see cref="Blueprint.Http.IResourceLinkGenerator"/> implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder AddResourceLinkGenerator<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, Blueprint.Http.IResourceLinkGenerator
    {
        this.Services.AddScoped<Blueprint.Http.IResourceLinkGenerator, T>();
        return this;
    }

    /// <summary>
    /// Turns off HATEOAS link population for this app, so its resources are written with empty <c>$links</c>, as a
    /// legacy Blueprint app that never called <c>AddHateoasLinks()</c> wrote them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Links are populated by default wherever link metadata exists. Legacy Blueprint linked only in an app that called
    /// <c>AddHateoasLinks()</c> (Signature365 <c>8b40ecc961</c>, <c>Blueprint.Http/BlueprintApiBuilderExtensions.cs:101-111</c>);
    /// any other app had no link step, so every resource it returned, and every one it audited, carried
    /// <c>"$links":{}</c>. Call this when migrating such an app, to keep that output. The migration tooling cannot do
    /// it for you: <c>BPMIG002</c> removes an <c>AddHateoasLinks()</c> that is present, which is safe because linking is
    /// the default, but no tool can see a call that is absent.
    /// </para>
    /// <para>
    /// Every pipeline of the app skips the link step, nested ones (such as a resource event's self query) included, and
    /// no registered <c>IResourceLinkGenerator</c> runs. Links a handler adds itself are kept. A resource event's
    /// <c>href</c>, the root-metadata document, the OpenAPI <c>x-links</c>, and <c>IApiLinkGenerator</c> for handlers are
    /// unaffected, as legacy's were. A <c>201</c>'s <c>Location</c> header, which is taken from the created resource's
    /// <c>self</c> link, is not written unless the handler adds that link; legacy's resource events were <c>200</c>s with
    /// no <c>Location</c>.
    /// </para>
    /// <para>
    /// The setting belongs to the app registered by this <c>AddBlueprintApi</c> call, one app per service provider (see
    /// <see cref="BlueprintApp"/>'s <c>HateoasLinksDisabled</c>).
    /// </para>
    /// </remarks>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder DisableHateoasLinks()
    {
        this.HateoasLinksDisabled = true;
        return this;
    }

    /// <summary>
    /// Gets a value indicating whether <see cref="DisableHateoasLinks"/> has been called, which the app registered with
    /// this builder is given when it is created.
    /// </summary>
    internal bool HateoasLinksDisabled { get; private set; }

    /// <summary>
    /// Registers the <see cref="Blueprint.Http.ETagger"/> service that provides ETag-based conditional
    /// request handling for Blueprint HTTP operations. Requires <c>IHttpContextAccessor</c> to be
    /// registered (typically via <c>AddHttpContextAccessor()</c>).
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder AddETagSupport()
    {
        this.Services.AddHttpContextAccessor();
        this.Services.TryAddScoped<Blueprint.Http.ETagger>();
        return this;
    }

    /// <summary>
    /// Registers a resource event audit repository, which is handed every legacy <see cref="Blueprint.Http.ResourceEvent"/>
    /// a command raises unless the command's type carries <see cref="Blueprint.Auditing.DoNotAuditAttribute"/>, as
    /// legacy Blueprint's <c>ResourceEventHandler</c> handed it. A query's is not, as legacy's was not.
    /// </summary>
    /// <remarks>
    /// The repository is registered scoped and resolved from each operation's own services. The generated pipeline calls
    /// <see cref="Blueprint.Http.IResourceEventAuditRepository.AddAsync"/> last of its post-execution steps, once the event
    /// is stamped with its correlation id and operation, has its href and has its data expanded. It is called beside any
    /// <see cref="Blueprint.Auditing.IResourceEventAuditSink"/>, not in place of one, and best-effort: a repository that
    /// throws, or cannot be constructed, does not fail the operation, and is logged at Error by the operation's pipeline
    /// logger, as legacy's generated pipeline logged it. An app that registers none audits nothing and is not told, as a
    /// legacy app without <c>AddResourceEvents</c> was not: that call registered the repository with the pipeline step
    /// that used it (Signature365 <c>8b40ecc961</c>, <c>BlueprintApiBuilderExtensions.cs:113-120</c>).
    /// </remarks>
    /// <typeparam name="T">The <see cref="Blueprint.Http.IResourceEventAuditRepository"/> implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder AddResourceEventAuditRepository<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, Blueprint.Http.IResourceEventAuditRepository
    {
        this.Services.AddScoped<Blueprint.Http.IResourceEventAuditRepository, T>();
        return this;
    }

    /// <summary>
    /// Registers a recurring task scheduler that defines cron-based background task schedules.
    /// Multiple schedulers are supported and each will be queried for its set of schedules.
    /// </summary>
    /// <typeparam name="T">The <see cref="Blueprint.Tasks.IRecurringTaskScheduler"/> implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder AddRecurringTaskScheduler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, Blueprint.Tasks.IRecurringTaskScheduler
    {
        this.Services.AddSingleton<Blueprint.Tasks.IRecurringTaskScheduler, T>();
        return this;
    }

    /// <summary>
    /// Registers a recurring task provider that bridges Blueprint's recurring task model to
    /// a concrete scheduler infrastructure (e.g., Hangfire's <c>IRecurringJobManager</c>).
    /// </summary>
    /// <remarks>
    /// Also registers <see cref="Blueprint.Runtime.Tasks.RecurringTaskManager"/>, which a provider's periodic re-run (the
    /// Hangfire provider's <c>{DefaultQueue}:Scheduler</c> job) resolves through the job activator. Registering it here does
    /// not decide where that re-run happens: the job runs in whichever process's Hangfire server listens to its queue, and
    /// Hangfire.NetCore's activator builds the manager from that process's container even where it was never registered
    /// (<c>ActivatorUtilities.GetServiceOrCreateInstance</c>), with that process's schedulers, possibly none. The provider
    /// removes every job the current set no longer contains, so every process whose server listens to the default queue
    /// must register the provider and every one of the app's recurring task schedulers; one with the provider and no
    /// schedulers removes all of the app's recurring jobs.
    /// </remarks>
    /// <typeparam name="T">The <see cref="Blueprint.Tasks.IRecurringTaskProvider"/> implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder AddRecurringTaskProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, Blueprint.Tasks.IRecurringTaskProvider
    {
        this.Services.AddSingleton<Blueprint.Tasks.IRecurringTaskProvider, T>();
        this.Services.TryAddScoped<Blueprint.Runtime.Tasks.RecurringTaskManager>();
        this.Services.AddHostedService<Blueprint.Runtime.Tasks.RecurringTaskRegistrationService>();
        return this;
    }

    /// <summary>
    /// Configures background task infrastructure options such as the default queue name
    /// and retry count. Providers read these from <c>IOptions&lt;BlueprintTaskOptions&gt;</c>.
    /// </summary>
    /// <param name="configure">Action to configure task options.</param>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder ConfigureTasks(Action<Blueprint.Tasks.BlueprintTaskOptions> configure)
    {
        this.Services.Configure(configure);
        return this;
    }

    /// <summary>
    /// Configures Blueprint HTTP options such as the public host for link generation,
    /// exception detail exposure, and content negotiation behaviour.
    /// </summary>
    /// <param name="configure">A delegate to configure <see cref="HttpOperationResultExecutor"/>-level options.</param>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder ConfigureHttp(Action<BlueprintHttpOptions> configure)
    {
        this.Services.Configure(configure);
        return this;
    }

    /// <summary>
    /// Configures the <see cref="System.Text.Json.JsonSerializerOptions"/> that Blueprint uses
    /// when serializing HTTP response bodies. Use this to add NodaTime converters, custom naming
    /// policies, or other serializer customizations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Example — adding NodaTime support:
    /// <code>
    /// blueprint.ConfigureJson(json =&gt;
    /// {
    ///     json.SerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
    /// });
    /// </code>
    /// </para>
    /// <para>
    /// Blueprint starts with sensible defaults (camelCase, relaxed escaping, string enums).
    /// This method lets you layer additional configuration on top.
    /// </para>
    /// </remarks>
    /// <param name="configure">A delegate to configure <see cref="Blueprint.Http.BlueprintJsonOptions"/>.</param>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder ConfigureJson(Action<Blueprint.Http.BlueprintJsonOptions> configure)
    {
        this.Services.Configure(configure);
        return this;
    }

    /// <summary>
    /// Registers a resource expander that populates <c>Expandable&lt;T&gt;</c> properties
    /// when the client requests <c>?expand=PropertyName</c> or the property is marked
    /// <c>[AlwaysExpand]</c>. Multiple expanders can be registered; the generated expansion
    /// pipeline calls <see cref="Blueprint.Runtime.Expansion.IResourceExpander.CanExpand"/>
    /// to find the matching expander for each expandable resource type.
    /// </summary>
    /// <typeparam name="T">The <see cref="Blueprint.Runtime.Expansion.IResourceExpander"/> implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder AddResourceExpander<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, Blueprint.Runtime.Expansion.IResourceExpander
    {
        this.Services.AddSingleton<Blueprint.Runtime.Expansion.IResourceExpander, T>();
        return this;
    }

    /// <summary>
    /// Registers first-class tenant support: an <see cref="Blueprint.Tenancy.ITenantAccessor"/> that
    /// stores the current tenant per async context, and an <see cref="Blueprint.Tenancy.ITenantResolver"/>
    /// that extracts the tenant identifier from the authenticated user's claims.
    /// <para>
    /// The generated pipeline's <c>TenantModule</c> calls the resolver during the Population stage,
    /// sets the accessor, and directly assigns <c>TenantId</c> properties on operations that have one
    /// — all without runtime reflection. An app root with <c>[BlueprintAppTenancy(TenantIdType = ...)]</c> has only a
    /// <c>TenantId</c> of that type assigned.
    /// </para>
    /// </summary>
    /// <typeparam name="TAccessor">The <see cref="Blueprint.Tenancy.ITenantAccessor"/> implementation type.
    /// Use <see cref="Blueprint.Tenancy.AsyncLocalTenantAccessor"/> for the default <see cref="AsyncLocal{T}"/>-backed
    /// implementation.</typeparam>
    /// <typeparam name="TResolver">The <see cref="Blueprint.Tenancy.ITenantResolver"/> implementation that
    /// knows how to extract the tenant identifier from a <see cref="System.Security.Claims.ClaimsPrincipal"/>.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder AddTenancy<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TAccessor,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TResolver>()
        where TAccessor : class, Blueprint.Tenancy.ITenantAccessor
        where TResolver : class, Blueprint.Tenancy.ITenantResolver
    {
        this.Services.AddSingleton<Blueprint.Tenancy.ITenantAccessor, TAccessor>();
        this.Services.AddSingleton<Blueprint.Tenancy.ITenantResolver, TResolver>();
        return this;
    }
}

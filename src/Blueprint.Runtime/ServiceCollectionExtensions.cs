using System.Diagnostics.CodeAnalysis;
using Blueprint.Model;
using Blueprint.Runtime.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        where TApp : BlueprintApp
        where TExec : class
    {
        services.AddSingleton<TApp>();
        services.AddSingleton<TExec>();

        // Default link generation infrastructure
        services.TryAddScoped<Blueprint.Http.IApiLinkGenerator, Blueprint.Http.ApiLinkGenerator>();
        services.TryAddScoped<Blueprint.Http.IResourceLinkGenerator, Blueprint.Http.EntityOperationResourceLinkGenerator>();

        var builder = new BlueprintServiceBuilder(services);
        configure?.Invoke(builder);

        return services;
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
        this.Services.AddScoped<Blueprint.Persistence.IUnitOfWork, T>();
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
        this.Services.AddScoped<Blueprint.Messaging.IMessageOutbox, T>();
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
    /// </summary>
    /// <typeparam name="T">The <see cref="Blueprint.Authorization.IApiAuthoriser"/> implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder AddApiAuthoriser<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, Blueprint.Authorization.IApiAuthoriser
    {
        this.Services.AddSingleton<Blueprint.Authorization.IApiAuthoriser, T>();

        // Ensure the aggregator is registered (idempotent via TryAdd)
        this.Services.TryAddSingleton<Blueprint.Authorization.IApiAuthoriserAggregator, Blueprint.Authorization.ApiAuthoriserAggregator>();

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
    /// <typeparam name="T">The <see cref="Blueprint.Tasks.IRecurringTaskProvider"/> implementation type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public BlueprintServiceBuilder AddRecurringTaskProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, Blueprint.Tasks.IRecurringTaskProvider
    {
        this.Services.AddSingleton<Blueprint.Tasks.IRecurringTaskProvider, T>();
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
}

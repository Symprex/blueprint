using System.Diagnostics.CodeAnalysis;
using Blueprint.Model;
using Blueprint.Runtime.Http;

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

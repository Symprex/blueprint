using Blueprint.Auditing;
using Blueprint.Authorization;
using Blueprint.Http;
using Blueprint.Runtime.Middleware;
using Blueprint.Tenancy;
using Microsoft.Extensions.Options;

namespace Blueprint.Runtime.Exec;

/// <summary>
/// Bundles the cross-cutting services that every generated pipeline needs so they are resolved once
/// at executor construction time rather than once per pipeline. For an application with 540 operations
/// this eliminates ~6,000 redundant DI lookups at startup (12 services × 540 pipelines → 12 + 540).
/// </summary>
/// <remarks>
/// <para>
/// Created inside the generated executor constructor and passed to each pipeline's
/// <see cref="OperationExecutorShell(ILoggerFactory, string, PipelineServices, bool)"/> overload.
/// Not registered in DI — the executor owns its lifetime, and all properties are effectively immutable
/// after construction because they capture singleton/root-scoped instances.
/// </para>
/// <para>
/// The <see cref="ServiceProvider"/> property is retained so that pipeline-specific cached services
/// (e.g. handler constructor dependencies discovered by <c>HandlerExecutionModule</c>) can still be
/// resolved during pipeline construction using the same root provider.
/// </para>
/// </remarks>
public sealed class PipelineServices
{
    /// <summary>
    /// Initialises a new <see cref="PipelineServices"/> by resolving all cross-cutting services from
    /// the supplied root <paramref name="serviceProvider"/>.
    /// </summary>
    /// <param name="serviceProvider">The root DI container built after all consumer registrations.</param>
    public PipelineServices(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        this.ServiceProvider = serviceProvider;
        this.TenantAccessor = serviceProvider.GetService<ITenantAccessor>();
        this.TenantResolver = serviceProvider.GetService<ITenantResolver>();
        this.PropertyPopulators = PipelineHelpers.ResolveServiceArray<IPropertyPopulator>(serviceProvider);
        this.AuditSink = serviceProvider.GetService<IAuditSink>();
        this.ResourceAuthorizationProvider = serviceProvider.GetService<IResourceAuthorizationProvider>();
        this.ApiAuthorisers = PipelineHelpers.ResolveServiceArray<IApiAuthoriser>(serviceProvider);
        this.OperationAuthorisers = PipelineHelpers.ResolveServiceArray<IOperationAuthoriser>(serviceProvider);
        this.UserAuthorisationContextFactory = serviceProvider.GetService<IUserAuthorisationContextFactory>();
        this.ResourceEventAuditSink = serviceProvider.GetService<IResourceEventAuditSink>();

        var blueprintJsonOpts = serviceProvider.GetService<IOptions<BlueprintJsonOptions>>();
        this.BodyJsonOptions = blueprintJsonOpts?.Value?.SerializerOptions;
    }

    /// <summary>
    /// The root service provider for resolving pipeline-specific cached services that are not part
    /// of the common cross-cutting set (e.g. handler constructor dependencies).
    /// </summary>
    public IServiceProvider ServiceProvider { get; }

    /// <inheritdoc cref="OperationExecutorShell.TenantAccessor"/>
    public ITenantAccessor? TenantAccessor { get; }

    /// <inheritdoc cref="OperationExecutorShell.TenantResolver"/>
    public ITenantResolver? TenantResolver { get; }

    /// <inheritdoc cref="OperationExecutorShell.PropertyPopulators"/>
    public IPropertyPopulator[] PropertyPopulators { get; }

    /// <inheritdoc cref="OperationExecutorShell.AuditSink"/>
    public IAuditSink? AuditSink { get; }

    /// <inheritdoc cref="OperationExecutorShell.ResourceAuthorizationProvider"/>
    public IResourceAuthorizationProvider? ResourceAuthorizationProvider { get; }

    /// <inheritdoc cref="OperationExecutorShell.ApiAuthorisers"/>
    public IApiAuthoriser[] ApiAuthorisers { get; }

    /// <inheritdoc cref="OperationExecutorShell.OperationAuthorisers"/>
    public IOperationAuthoriser[] OperationAuthorisers { get; }

    /// <inheritdoc cref="OperationExecutorShell.UserAuthorisationContextFactory"/>
    public IUserAuthorisationContextFactory? UserAuthorisationContextFactory { get; }

    /// <inheritdoc cref="OperationExecutorShell.ResourceEventAuditSink"/>
    public IResourceEventAuditSink? ResourceEventAuditSink { get; }

    /// <inheritdoc cref="OperationExecutorShell.BodyJsonOptions"/>
    public System.Text.Json.JsonSerializerOptions? BodyJsonOptions { get; }
}

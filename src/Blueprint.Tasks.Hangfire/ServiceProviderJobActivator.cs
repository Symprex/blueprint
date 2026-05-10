using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.Tasks.Hangfire;

/// <summary>
/// A Hangfire <see cref="JobActivator"/> that uses Microsoft's DI
/// <see cref="IServiceProvider"/> to resolve job types and create per-job scopes.
/// </summary>
public sealed class ServiceProviderJobActivator : JobActivator
{
    private readonly IServiceProvider _rootServiceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceProviderJobActivator"/> class.
    /// </summary>
    /// <param name="rootServiceProvider">The root service provider for resolving job activations.</param>
    public ServiceProviderJobActivator(IServiceProvider rootServiceProvider)
    {
        this._rootServiceProvider = rootServiceProvider ?? throw new ArgumentNullException(nameof(rootServiceProvider));
    }

    /// <inheritdoc />
    public override object ActivateJob(Type jobType)
    {
        return this._rootServiceProvider.GetRequiredService(jobType);
    }

    /// <inheritdoc />
    public override JobActivatorScope BeginScope(JobActivatorContext context)
    {
        return new ServiceProviderScope(this._rootServiceProvider.CreateScope());
    }

    private sealed class ServiceProviderScope : JobActivatorScope
    {
        private readonly IServiceScope _scope;

        public ServiceProviderScope(IServiceScope scope)
        {
            this._scope = scope;
        }

        public override object Resolve(Type type)
        {
            return this._scope.ServiceProvider.GetRequiredService(type);
        }

        public override void DisposeScope()
        {
            this._scope.Dispose();
        }
    }
}

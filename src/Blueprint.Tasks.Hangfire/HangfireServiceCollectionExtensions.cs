using Blueprint.Tasks.Hangfire;
using Hangfire;
using JetBrains.Annotations;

// In Blueprint.Tasks namespace so consumers discover it alongside other task registration.
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods to register Blueprint's Hangfire integration with the DI container.
/// </summary>
[PublicAPI]
public static class HangfireServiceCollectionExtensions
{
    private static readonly object _filterLock = new();
    private static bool _filterConfigured;

    /// <summary>
    /// Registers Hangfire as the background task and recurring task provider for Blueprint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hangfire itself must already be configured separately via <c>services.AddHangfire(…)</c>
    /// and <c>services.AddHangfireServer(…)</c>. This method wires Blueprint's scheduling
    /// abstractions to Hangfire's job client and recurring job manager.
    /// </para>
    /// </remarks>
    /// <param name="builder">The Blueprint service builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static BlueprintServiceBuilder UseHangfire(this BlueprintServiceBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Register the schedule provider (bridges IBackgroundTaskScheduleProvider → Hangfire IBackgroundJobClient).
        builder.AddBackgroundTaskScheduleProvider<HangfireBackgroundTaskScheduleProvider>();

        // Register the recurring task provider (bridges IRecurringTaskProvider → Hangfire IRecurringJobManager).
        builder.AddRecurringTaskProvider<HangfireRecurringTaskProvider>();

        // Register the Hangfire-specific executor that Hangfire deserializes jobs into.
        builder.Services.AddScoped<HangfireTaskExecutor>();

        // Replace Hangfire's default AutomaticRetryAttribute with our filter that reads
        // per-task [AutomaticRetry(N)] from the task type.
        ConfigureRetryFilter(builder.Services);

        return builder;
    }

    /// <summary>
    /// Configures Hangfire to use the application's <see cref="IServiceProvider"/> as the job activator.
    /// Call this in your Hangfire configuration: <c>config.UseServiceProviderActivator(serviceProvider)</c>.
    /// </summary>
    /// <param name="configuration">The Hangfire global configuration.</param>
    /// <param name="serviceProvider">The root service provider.</param>
    /// <returns>The configured activator for chaining.</returns>
    public static IGlobalConfiguration<ServiceProviderJobActivator> UseServiceProviderActivator(
        this IGlobalConfiguration configuration,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        return configuration.UseActivator(new ServiceProviderJobActivator(serviceProvider));
    }

    private static void ConfigureRetryFilter(IServiceCollection services)
    {
        if (_filterConfigured)
        {
            return;
        }

        lock (_filterLock)
        {
            if (_filterConfigured)
            {
                return;
            }

            // Remove Hangfire's default AutomaticRetryAttribute so we can substitute ours.
            var existing = GlobalJobFilters.Filters
                .FirstOrDefault(static f => f.Instance is global::Hangfire.AutomaticRetryAttribute);

            if (existing is not null)
            {
                GlobalJobFilters.Filters.Remove(existing.Instance);
            }

            // Use a deferred approach: we need BlueprintTaskOptions to know the default retry count.
            // At registration time options aren't built yet, so we use a safe default of 5 and
            // let the filter read per-task overrides at runtime.
            GlobalJobFilters.Filters.Add(new TaskAutomaticRetryJobFilter(defaultRetryCount: 5), -20);

            _filterConfigured = true;
        }
    }
}

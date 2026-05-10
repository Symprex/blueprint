using Blueprint.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.Hangfire;

/// <summary>
/// Extension methods for registering Blueprint's Hangfire integration with the DI container.
/// </summary>
[PublicAPI]
public static class HangfireServiceCollectionExtensions
{
    /// <summary>
    /// Registers Blueprint's Hangfire integration, providing <see cref="IBackgroundTaskScheduleProvider"/>
    /// and <see cref="IRecurringTaskProvider"/> implementations backed by Hangfire.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This registration assumes Hangfire itself has already been configured (e.g. via
    /// <c>services.AddHangfire(…)</c> and <c>services.AddHangfireServer()</c>). Blueprint.Hangfire
    /// layers on top by registering:
    /// <list type="bullet">
    /// <item><see cref="HangfireBackgroundTaskScheduleProvider"/> as the
    /// <see cref="IBackgroundTaskScheduleProvider"/> for immediate and scheduled task dispatch.</item>
    /// <item><see cref="HangfireRecurringTaskProvider"/> as the <see cref="IRecurringTaskProvider"/>
    /// for cron-based recurring schedule management.</item>
    /// <item><see cref="HangfireBackgroundTaskExecutor"/> as the Hangfire job target that
    /// deserializes and executes tasks through Blueprint's generated pipeline.</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddBlueprintHangfire(this IServiceCollection services)
    {
        services.AddScoped<IBackgroundTaskScheduleProvider, HangfireBackgroundTaskScheduleProvider>();
        services.AddScoped<IRecurringTaskProvider, HangfireRecurringTaskProvider>();
        services.AddScoped<HangfireBackgroundTaskExecutor>();

        return services;
    }
}

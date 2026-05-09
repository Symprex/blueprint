using Blueprint.Tasks;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// An example <see cref="IRecurringTaskScheduler"/> that declares cron-based recurring tasks.
/// This demonstrates the consumer seam for Hangfire-style recurring job registration:
/// in production, the <see cref="IRecurringTaskProvider"/> bridges these schedules to
/// Hangfire's <c>IRecurringJobManager.AddOrUpdate</c>.
/// </summary>
/// <remarks>
/// <para>
/// In a Signature365-style consumer with Hangfire, register via:
/// <code>
/// blueprint.AddRecurringTaskScheduler&lt;SampleRecurringTaskScheduler&gt;();
/// blueprint.AddRecurringTaskProvider&lt;HangfireRecurringTaskProvider&gt;();
/// </code>
/// </para>
/// <para>
/// The Hangfire recurring task provider (consumer-owned) would call
/// <c>IRecurringJobManager.AddOrUpdate</c> for each <see cref="RecurringTaskSchedule"/>,
/// serializing the <see cref="IBackgroundTask"/> and its cron expression.
/// </para>
/// </remarks>
public sealed class SampleRecurringTaskScheduler(ILogger<SampleRecurringTaskScheduler> logger) : IRecurringTaskScheduler
{
    /// <inheritdoc />
    public Task<IEnumerable<RecurringTaskSchedule>> GetTaskSchedulesAsync()
    {
        logger.LogInformation("Discovering recurring task schedules");

        // Use the existing sample background task to demonstrate recurring scheduling.
        // In production, each schedule would reference a dedicated recurring task type.
        var schedules = new List<RecurringTaskSchedule>
        {
            // Example: a daily notification task at 2am UTC
            new(
                "daily-notification-check",
                new Sample.Contract.Api.SendTemplateSharedNotificationTask
                {
                    TemplateId = "recurring-check",
                    RecipientEmail = "system@example.com",
                },
                Cron.Daily(2)),
        };

        return Task.FromResult<IEnumerable<RecurringTaskSchedule>>(schedules);
    }
}

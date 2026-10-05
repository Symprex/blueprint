using Blueprint.Tasks;
using FluentAssertions;
using Hangfire;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Blueprint.Hangfire.Tests;

/// <summary>
/// Proves that <see cref="HangfireRecurringTaskProvider"/> correctly registers recurring schedules
/// with Hangfire's <see cref="IRecurringJobManager"/>.
/// </summary>
public class HangfireRecurringTaskProviderTests
{
    [Fact]
    public async Task RegisterSchedulesAsync_registers_all_schedules()
    {
        var jobManager = Substitute.For<IRecurringJobManager>();
        var logger = NullLogger<HangfireRecurringTaskProvider>.Instance;
        var sut = new HangfireRecurringTaskProvider(jobManager, logger);

        var task1 = new TestRecurringBackgroundTask();
        var task2 = new TestRecurringBackgroundTask();

        var schedules = new List<RecurringTaskSchedule>
        {
            new("daily-cleanup", task1, "0 2 * * *"),
            new("every-five-min", task2, "*/5 * * * *"),
        };

        await sut.RegisterSchedulesAsync(schedules);

        // IRecurringJobManager.AddOrUpdate(string, string, Job, string, RecurringJobOptions) is called
        // via the extension method. Verify a call was received for each schedule, besides the reschedule job:
        // Each under its id, which for a schedule no RecurringTaskManager collected is its name (legacy registered the id it was
        // given, HangfireRecurringTaskProvider.cs:85-86). No default queue is set, so the reschedule job is legacy's
        // ":Scheduler" (:45).
        RecurringJobIds(jobManager).Should().BeEquivalentTo("daily-cleanup", "every-five-min", ":Scheduler");
    }

    [Fact]
    public async Task RegisterSchedulesAsync_with_empty_schedules_installs_only_the_reschedule_job()
    {
        var jobManager = Substitute.For<IRecurringJobManager>();
        var logger = NullLogger<HangfireRecurringTaskProvider>.Instance;
        var sut = new HangfireRecurringTaskProvider(jobManager, logger);

        await sut.RegisterSchedulesAsync([]);

        RecurringJobIds(jobManager).Should().Equal(":Scheduler");
    }

    private static IEnumerable<string> RecurringJobIds(IRecurringJobManager jobManager)
    {
        return jobManager.ReceivedCalls()
            .Where(static call => call.GetMethodInfo().Name == nameof(IRecurringJobManager.AddOrUpdate))
            .Select(static call => (string)call.GetArguments()[0]!);
    }

    private class TestRecurringBackgroundTask : IBackgroundTask
    {
        public static Blueprint.Model.ApiOperationDescriptor Configure()
        {
            return new(typeof(TestRecurringBackgroundTask), "TestRecurringBackgroundTask", "test");
        }
    }
}

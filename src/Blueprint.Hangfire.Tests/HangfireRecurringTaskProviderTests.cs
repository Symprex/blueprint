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
        // via the extension method. Verify any call was received for each schedule:
        jobManager.ReceivedCalls().Should().HaveCount(2);
    }

    [Fact]
    public async Task RegisterSchedulesAsync_with_empty_schedules_does_nothing()
    {
        var jobManager = Substitute.For<IRecurringJobManager>();
        var logger = NullLogger<HangfireRecurringTaskProvider>.Instance;
        var sut = new HangfireRecurringTaskProvider(jobManager, logger);

        await sut.RegisterSchedulesAsync([]);

        jobManager.ReceivedCalls().Should().BeEmpty();
    }

    private class TestRecurringBackgroundTask : IBackgroundTask
    {
        public static Blueprint.Model.ApiOperationDescriptor Configure()
        {
            return new(typeof(TestRecurringBackgroundTask), "TestRecurringBackgroundTask", "test");
        }
    }
}

using Blueprint.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Tasks;

/// <summary>
/// Proves the <see cref="Blueprint.Runtime.Tasks.RecurringTaskRegistrationService"/> hosted service
/// correctly discovers all registered <see cref="IRecurringTaskScheduler"/> implementations and
/// pushes their schedules to the <see cref="IRecurringTaskProvider"/>. This is the consumer seam
/// used by Hangfire-style recurring job infrastructure.
/// </summary>
public class RecurringTaskRegistrationServiceTests
{
    [Fact]
    public async Task When_started_with_scheduler_and_provider_then_registers_all_schedules()
    {
        var provider = new TestRecurringTaskProvider();
        var host = BuildHost(
            [new TestRecurringTaskScheduler("job-a", "0 2 * * *"), new TestRecurringTaskScheduler("job-b", "*/5 * * * *")],
            provider);

        await host.StartAsync();
        await host.StopAsync();

        provider.RegisteredSchedules.Should().HaveCount(2);
        provider.RegisteredSchedules.Should().Contain(static s => s.Name == "job-a" && s.CronExpression == "0 2 * * *");
        provider.RegisteredSchedules.Should().Contain(static s => s.Name == "job-b" && s.CronExpression == "*/5 * * * *");
    }

    [Fact]
    public async Task When_started_without_provider_then_no_error_and_no_registration()
    {
        var host = BuildHost(
            [new TestRecurringTaskScheduler("orphan-job", "0 0 * * *")],
            provider: null);

        // Should not throw even though no provider is registered
        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public async Task When_no_schedulers_registered_then_provider_is_not_called()
    {
        var provider = new TestRecurringTaskProvider();
        var host = BuildHost([], provider);

        await host.StartAsync();
        await host.StopAsync();

        provider.RegisteredSchedules.Should().BeEmpty("provider should not be called with empty schedules");
        provider.RegisterCallCount.Should().Be(0);
    }

    [Fact]
    public async Task When_multiple_schedulers_registered_then_all_schedules_are_aggregated()
    {
        var provider = new TestRecurringTaskProvider();
        var schedulers = new IRecurringTaskScheduler[]
        {
            new TestRecurringTaskScheduler("scheduler-1-task", "0 1 * * *"),
            new MultiTaskScheduler(
            [
                ("multi-a", "0 6 * * MON"),
                ("multi-b", "0 12 * * *"),
            ]),
        };

        var host = BuildHost(schedulers, provider);

        await host.StartAsync();
        await host.StopAsync();

        provider.RegisteredSchedules.Should().HaveCount(3);
        provider.RegisteredSchedules.Select(static s => s.Name)
            .Should().BeEquivalentTo(["scheduler-1-task", "multi-a", "multi-b"]);
    }

    [Fact]
    public async Task When_schedule_has_queue_and_timezone_then_provider_receives_full_metadata()
    {
        var provider = new TestRecurringTaskProvider();
        var scheduler = new CustomMetadataScheduler();
        var host = BuildHost([scheduler], provider);

        await host.StartAsync();
        await host.StopAsync();

        provider.RegisteredSchedules.Should().HaveCount(1);
        var schedule = provider.RegisteredSchedules[0];
        schedule.Name.Should().Be("queued-task");
        schedule.Queue.Should().Be("priority");
        schedule.TimeZone.Id.Should().Be("Pacific Standard Time");
        schedule.CronExpression.Should().Be("0 9 * * *");
    }

    private static IHost BuildHost(
        IRecurringTaskScheduler[] schedulers,
        IRecurringTaskProvider? provider)
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
                services.AddHostedService<Blueprint.Runtime.Tasks.RecurringTaskRegistrationService>();

                foreach (var scheduler in schedulers)
                {
                    services.AddSingleton(scheduler);
                }

                if (provider is not null)
                {
                    services.AddSingleton(provider);
                }
            });

        return builder.Build();
    }

    private sealed class TestRecurringTaskScheduler(string name, string cronExpression) : IRecurringTaskScheduler
    {
        public Task<IEnumerable<RecurringTaskSchedule>> GetTaskSchedulesAsync()
        {
            var task = new RecurringTestBackgroundTask();
            IEnumerable<RecurringTaskSchedule> schedules = [new RecurringTaskSchedule(name, task, cronExpression)];
            return Task.FromResult(schedules);
        }
    }

    private sealed class MultiTaskScheduler((string Name, string Cron)[] tasks) : IRecurringTaskScheduler
    {
        public Task<IEnumerable<RecurringTaskSchedule>> GetTaskSchedulesAsync()
        {
            IEnumerable<RecurringTaskSchedule> schedules = tasks
                .Select(static t => new RecurringTaskSchedule(t.Name, new RecurringTestBackgroundTask(), t.Cron))
                .ToList();
            return Task.FromResult(schedules);
        }
    }

    private sealed class CustomMetadataScheduler : IRecurringTaskScheduler
    {
        public Task<IEnumerable<RecurringTaskSchedule>> GetTaskSchedulesAsync()
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
            IEnumerable<RecurringTaskSchedule> schedules =
            [
                new RecurringTaskSchedule("queued-task", new RecurringTestBackgroundTask(), "0 9 * * *", tz, "priority"),
            ];
            return Task.FromResult(schedules);
        }
    }

    private sealed class TestRecurringTaskProvider : IRecurringTaskProvider
    {
        public List<RecurringTaskSchedule> RegisteredSchedules { get; } = [];

        public int RegisterCallCount { get; private set; }

        public Task RegisterSchedulesAsync(IReadOnlyList<RecurringTaskSchedule> schedules, CancellationToken cancellationToken = default)
        {
            this.RegisterCallCount++;
            this.RegisteredSchedules.AddRange(schedules);
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// A minimal background task used by recurring task registration tests.
/// Requires partial + inline handler to satisfy the source generator.
/// </summary>
public partial class RecurringTestBackgroundTask : IBackgroundTask
{
    public Blueprint.Runtime.Exec.OkResult<string> Handle()
    {
        return new Blueprint.Runtime.Exec.OkResult<string>("ok");
    }
}

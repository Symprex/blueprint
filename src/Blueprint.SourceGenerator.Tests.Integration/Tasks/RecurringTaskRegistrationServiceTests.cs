using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
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
    public async Task When_no_schedules_are_declared_then_the_provider_is_given_the_empty_set()
    {
        // Legacy RecurringTaskManager.RescheduleAllAsync always handed the provider the current set, empty or not, and
        // the provider removed every job not in it, so the app's last schedule to be deleted from code stopped firing.
        var provider = new TestRecurringTaskProvider();
        var host = BuildHost([], provider);

        await host.StartAsync();
        await host.StopAsync();

        provider.RegisterCallCount.Should().Be(1, "an empty set is the current set, and the provider must learn of it");
        provider.RegisteredSchedules.Should().BeEmpty();
    }

    [Fact]
    public async Task When_a_scheduler_throws_then_it_declares_no_schedules_and_the_others_are_still_registered()
    {
        // Legacy logged the failure and treated the scheduler as declaring nothing ("Will delete all existing jobs").
        var provider = new TestRecurringTaskProvider();
        var host = BuildHost(
            [new ThrowingScheduler(), new TestRecurringTaskScheduler("still-registered", "0 3 * * *")],
            provider);

        var start = () => host.StartAsync();

        await start.Should().NotThrowAsync("one scheduler's failure must not stop the host or the other schedulers");
        await host.StopAsync();

        provider.RegisterCallCount.Should().Be(1);
        provider.RegisteredSchedules.Select(static s => s.Name).Should().Equal("still-registered");
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

    [Fact]
    public async Task AddRecurringTaskProvider_registers_the_manager_the_periodic_reschedule_job_activates()
    {
        // The provider's reschedule job names RecurringTaskManager, which the job activator resolves from the container.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(
            static blueprint => blueprint.AddRecurringTaskProvider<TestRecurringTaskProvider>());
        services.AddSingleton<IRecurringTaskScheduler>(new TestRecurringTaskScheduler("periodic", "0 4 * * *"));

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var manager = scope.ServiceProvider.GetRequiredService<Blueprint.Runtime.Tasks.RecurringTaskManager>();
        await manager.RescheduleAllAsync(CancellationToken.None);

        var registered = (TestRecurringTaskProvider)provider.GetRequiredService<IRecurringTaskProvider>();
        registered.RegisteredSchedules.Select(static s => s.Name).Should().Equal("periodic");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_provider_registered_scoped_beside_AddRecurringTaskProvider_is_started_in_a_scope_of_its_own(bool scopedRegisteredLast)
    {
        // Blueprint.Hangfire's AddBlueprintHangfire registers its IRecurringTaskProvider as scoped, as legacy registered it,
        // and an app with recurring tasks calls AddRecurringTaskProvider too, which registers a singleton provider and this
        // singleton hosted service. Whichever registration comes last is the one resolved. Legacy's hosted service resolved
        // the provider in a scope of its own (RecurringJobManagerRegistrationHostedService).
        var registered = new List<string>();
        var services = new ServiceCollection();
        services.AddLogging();

        if (!scopedRegisteredLast)
        {
            services.AddScoped<IRecurringTaskProvider>(_ => new ScopedRecurringTaskProvider(registered));
        }

        services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(
            static blueprint => blueprint.AddRecurringTaskProvider<TestRecurringTaskProvider>());

        if (scopedRegisteredLast)
        {
            services.AddScoped<IRecurringTaskProvider>(_ => new ScopedRecurringTaskProvider(registered));
        }

        services.AddSingleton<IRecurringTaskScheduler>(new TestRecurringTaskScheduler("periodic", "0 4 * * *"));

        // As an ASP.NET Core host builds its container in development.
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        foreach (var hostedService in provider.GetServices<IHostedService>().OfType<Blueprint.Runtime.Tasks.RecurringTaskRegistrationService>())
        {
            await hostedService.StartAsync(CancellationToken.None);
        }

        if (scopedRegisteredLast)
        {
            registered.Should().Equal("periodic");
        }
        else
        {
            using var scope = provider.CreateScope();
            ((TestRecurringTaskProvider)scope.ServiceProvider.GetRequiredService<IRecurringTaskProvider>()).RegisteredSchedules
                .Select(static s => s.Name).Should().Equal("periodic");
        }
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

    private sealed class ThrowingScheduler : IRecurringTaskScheduler
    {
        public Task<IEnumerable<RecurringTaskSchedule>> GetTaskSchedulesAsync()
        {
            throw new InvalidOperationException("The scheduler could not load its schedules.");
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

    /// <summary>A provider registered scoped, as Blueprint.Hangfire's is, recording what it is given.</summary>
    private sealed class ScopedRecurringTaskProvider(List<string> registered) : IRecurringTaskProvider
    {
        public Task RegisterSchedulesAsync(IReadOnlyList<RecurringTaskSchedule> schedules, CancellationToken cancellationToken = default)
        {
            registered.AddRange(schedules.Select(static s => s.Name));
            return Task.CompletedTask;
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

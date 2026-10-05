using Blueprint.Runtime.Tasks;
using Blueprint.Tasks;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Blueprint.Hangfire.Tests;

/// <summary>
/// Proves what the host's start-up does with <see cref="HangfireRecurringTaskProvider"/>: it installs the
/// <c>{DefaultQueue}:Scheduler</c> job and nothing else, as legacy Blueprint's start-up did. The schedules are registered,
/// and the jobs of those no longer declared removed, only when that job runs.
/// </summary>
/// <remarks>
/// Legacy's start-up hook called only <c>IRecurringTaskProvider.SetupRecurringManagerAsync</c> (Signature365
/// <c>8b40ecc961</c>, <c>Blueprint.Tasks/RecurringJobManagerRegistrationHostedService.cs:37-43</c>). Its Hangfire provider
/// implemented that as one <c>AddOrUpdate</c> of <c>{DefaultQueue}:Scheduler</c>, cron <c>*/30 * * * *</c>, on the default
/// queue, targeting <c>RecurringTaskManager.RescheduleAllAsync</c> (<c>Blueprint.Tasks.Hangfire/HangfireRecurringTaskProvider.cs:110-127</c>).
/// Registering the schedules and removing stale jobs (<c>UpdateAsync</c>, <c>:50-107</c>) ran only from
/// <c>RescheduleAllAsync</c> (<c>Blueprint.Tasks/RecurringTaskManager.cs:55-68</c>), that is, from the job, serialised
/// behind Hangfire's retries, never from a replica's start-up.
/// </remarks>
public class HangfireRecurringTaskStartupTests
{
    private const string AppQueue = "portal";

    [Fact]
    public async Task Start_up_installs_only_the_scheduler_job_and_neither_registers_nor_removes_any_schedule()
    {
        var (manager, calls) = CreateManager();
        var scheduler = new CountingScheduler("nightly");

        await using var services = BuildServices(manager, CreateStorage("blueprint:removed-from-code"), scheduler);

        await StartHostedServicesAsync(services);

        var (id, job, cron) = calls.Should().ContainSingle("start-up adds or updates the scheduler job and no other").Subject;
        id.Should().Be("portal:Scheduler");
        cron.Should().Be("*/30 * * * *");
        job.Queue.Should().Be(AppQueue);
        job.Type.Should().Be(typeof(RecurringTaskManager));
        job.Method.Name.Should().Be(nameof(RecurringTaskManager.RescheduleAllAsync));

        manager.DidNotReceiveWithAnyArgs().RemoveIfExists(default!);
        scheduler.Calls.Should().Be(0, "start-up does not ask a scheduler for its schedules; the scheduler job does");
    }

    [Fact]
    public async Task A_host_whose_scheduler_cannot_produce_its_schedules_still_starts()
    {
        // Legacy's start-up never asked a scheduler for its schedules (see the remarks), so a scheduler that could not
        // produce them, however it failed, could not stop the host starting. This one fails while its schedules are
        // enumerated, outside the scheduler call that RecurringTaskManager guards.
        var (manager, calls) = CreateManager();

        await using var services = BuildServices(manager, CreateStorage(), new LazilyThrowingScheduler());

        var start = () => StartHostedServicesAsync(services);

        await start.Should().NotThrowAsync();
        calls.Select(static c => c.Id).Should().Equal("portal:Scheduler");
    }

    private static ServiceProvider BuildServices(IRecurringJobManager manager, JobStorage storage, IRecurringTaskScheduler scheduler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(scheduler);

        // Scoped, as AddBlueprintHangfire registers it.
        services.AddScoped<IRecurringTaskProvider>(_ => new HangfireRecurringTaskProvider(
            manager,
            storage,
            Options.Create(new BlueprintTaskOptions { DefaultQueue = AppQueue }),
            NullLogger<HangfireRecurringTaskProvider>.Instance));
        services.AddHostedService<RecurringTaskRegistrationService>();

        // As an ASP.NET Core host builds its container in development.
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static async Task StartHostedServicesAsync(IServiceProvider services)
    {
        foreach (var hostedService in services.GetServices<IHostedService>())
        {
            await hostedService.StartAsync(CancellationToken.None);
        }
    }

    private static (IRecurringJobManager Manager, List<(string Id, Job Job, string Cron)> Calls) CreateManager()
    {
        var calls = new List<(string Id, Job Job, string Cron)>();
        var manager = Substitute.For<IRecurringJobManager>();
        manager
            .When(static m => m.AddOrUpdate(Arg.Any<string>(), Arg.Any<Job>(), Arg.Any<string>(), Arg.Any<RecurringJobOptions>()))
            .Do(call => calls.Add((call.ArgAt<string>(0), call.ArgAt<Job>(1), call.ArgAt<string>(2))));

        return (manager, calls);
    }

    private static JobStorage CreateStorage(params string[] existingRecurringJobIds)
    {
        var connection = Substitute.For<IStorageConnection>();
        connection.GetAllItemsFromSet("recurring-jobs").Returns(_ => new HashSet<string>(existingRecurringJobIds));

        var storage = Substitute.For<JobStorage>();
        storage.GetConnection().Returns(connection);

        return storage;
    }

    public sealed class StartupTestTask : IBackgroundTask
    {
        public static Blueprint.Model.ApiOperationDescriptor Configure()
        {
            return new(typeof(StartupTestTask), nameof(StartupTestTask), "test");
        }
    }

    private sealed class CountingScheduler(string name) : IRecurringTaskScheduler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref this._calls);

        public Task<IEnumerable<RecurringTaskSchedule>> GetTaskSchedulesAsync()
        {
            Interlocked.Increment(ref this._calls);

            return Task.FromResult<IEnumerable<RecurringTaskSchedule>>([new RecurringTaskSchedule(name, new StartupTestTask(), "0 2 * * *")]);
        }
    }

    private sealed class LazilyThrowingScheduler : IRecurringTaskScheduler
    {
        public Task<IEnumerable<RecurringTaskSchedule>> GetTaskSchedulesAsync()
        {
            return Task.FromResult(Schedules());

            static IEnumerable<RecurringTaskSchedule> Schedules()
            {
                yield return new RecurringTaskSchedule("first", new StartupTestTask(), "0 2 * * *");

                throw new InvalidOperationException("The scheduler could not load the rest of its schedules.");
            }
        }
    }
}

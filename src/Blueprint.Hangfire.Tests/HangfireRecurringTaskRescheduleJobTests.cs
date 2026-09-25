using Blueprint.Runtime.Tasks;
using Blueprint.Tasks;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Blueprint.Hangfire.Tests;

/// <summary>
/// Proves that <see cref="HangfireRecurringTaskProvider"/> installs the recurring job that re-runs
/// <see cref="RecurringTaskManager.RescheduleAllAsync"/> every 30 minutes, as legacy Blueprint's Hangfire provider did
/// (<c>SetupRecurringManagerAsync</c>): id <c>{DefaultQueue}:Scheduler</c>, cron <c>*/30 * * * *</c>, on the app's
/// default queue.
/// </summary>
/// <remarks>
/// The re-run is what applies a schedule a scheduler starts or stops declaring while the app runs, and what retries a
/// registration that failed. It is installed as part of registration, so only the process that registers the app's
/// schedules installs it, as legacy installed it only where recurring tasks were enabled.
/// </remarks>
public class HangfireRecurringTaskRescheduleJobTests
{
    private const string AppQueue = "portal";

    [Fact]
    public async Task Registration_installs_the_reschedule_job_every_30_minutes_on_the_apps_default_queue()
    {
        var (manager, calls) = CreateManager();
        var sut = CreateProvider(manager, CreateStorage());

        await sut.RegisterSchedulesAsync([Schedule("nightly")]);

        var (id, job, cron) = calls.Should().ContainSingle(static c => c.Id == AppQueue + ":Scheduler").Subject;
        id.Should().Be("portal:Scheduler");
        cron.Should().Be("*/30 * * * *");
        job.Queue.Should().Be(AppQueue);
        job.Type.Should().Be(typeof(RecurringTaskManager));
        job.Method.Name.Should().Be(nameof(RecurringTaskManager.RescheduleAllAsync));
    }

    [Fact]
    public async Task With_no_default_queue_the_reschedule_job_is_legacys_colon_Scheduler_on_hangfires_default_queue()
    {
        // Legacy's option had no default (Blueprint.Tasks/BlueprintTaskOptions.cs:10-11 at Signature365 8b40ecc961), and its
        // provider built the id from it unchanged, "" + ":Scheduler" (Blueprint.Tasks.Hangfire/HangfireRecurringTaskProvider.cs:45),
        // but ran the job on Hangfire's default queue (:46, :116, :122).
        var (manager, calls) = CreateManager();
        var sut = new HangfireRecurringTaskProvider(
            manager,
            CreateStorage(),
            Options.Create(new BlueprintTaskOptions()),
            NullLogger<HangfireRecurringTaskProvider>.Instance);

        await sut.SetupRecurringManagerAsync();

        var (id, job, _) = calls.Should().ContainSingle().Subject;
        id.Should().Be(":Scheduler");
        job.Queue.Should().Be("default");
    }

    [Fact]
    public async Task The_reschedule_job_reschedules_every_declared_schedule_when_it_runs()
    {
        var (manager, calls) = CreateManager();
        var sut = CreateProvider(manager, CreateStorage());
        await sut.RegisterSchedulesAsync([]);
        var job = calls.Single(static c => c.Id == AppQueue + ":Scheduler").Job;

        var provider = new RecordingProvider();
        var target = new RecurringTaskManager(
            [new FixedScheduler("nightly")],
            provider,
            NullLogger<RecurringTaskManager>.Instance);

        // What Hangfire does when the job fires: invoke the method on an activated instance with the stored arguments.
        await (Task)job.Method.Invoke(target, job.Args.ToArray())!;

        provider.Registered.Should().ContainSingle().Which.Select(static s => s.Name).Should().Equal("nightly");
    }

    [Fact]
    public async Task The_reschedule_job_is_never_removed_as_a_stale_recurring_task()
    {
        var (manager, _) = CreateManager();
        var sut = CreateProvider(manager, CreateStorage("portal:Scheduler", "NightlyScheduler:removed-from-code"));

        await sut.RegisterSchedulesAsync([]);

        manager.DidNotReceive().RemoveIfExists("portal:Scheduler");
        manager.Received(1).RemoveIfExists("NightlyScheduler:removed-from-code");
    }

    [Fact]
    public async Task The_reschedule_job_is_installed_even_when_registering_a_schedule_fails()
    {
        // So the next run retries the registration, as legacy's separately installed scheduler job did.
        var (manager, calls) = CreateManager();
        manager
            .When(static m => m.AddOrUpdate("broken", Arg.Any<Job>(), Arg.Any<string>(), Arg.Any<RecurringJobOptions>()))
            .Do(static _ => throw new InvalidOperationException("The storage rejected the job."));
        var sut = CreateProvider(manager, CreateStorage());

        var register = () => sut.RegisterSchedulesAsync([Schedule("broken")]);

        await register.Should().ThrowAsync<InvalidOperationException>();
        calls.Should().Contain(static c => c.Id == AppQueue + ":Scheduler");
    }

    private static HangfireRecurringTaskProvider CreateProvider(IRecurringJobManager manager, JobStorage storage)
    {
        return new HangfireRecurringTaskProvider(
            manager,
            storage,
            Options.Create(new BlueprintTaskOptions { DefaultQueue = AppQueue }),
            NullLogger<HangfireRecurringTaskProvider>.Instance);
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

    private static RecurringTaskSchedule Schedule(string name)
    {
        return new RecurringTaskSchedule(name, new RescheduleTestTask(), "0 2 * * *");
    }

    public sealed class RescheduleTestTask : IBackgroundTask
    {
        public static Blueprint.Model.ApiOperationDescriptor Configure()
        {
            return new(typeof(RescheduleTestTask), nameof(RescheduleTestTask), "test");
        }
    }

    private sealed class FixedScheduler(string name) : IRecurringTaskScheduler
    {
        public Task<IEnumerable<RecurringTaskSchedule>> GetTaskSchedulesAsync()
        {
            return Task.FromResult<IEnumerable<RecurringTaskSchedule>>([Schedule(name)]);
        }
    }

    private sealed class RecordingProvider : IRecurringTaskProvider
    {
        public List<IReadOnlyList<RecurringTaskSchedule>> Registered { get; } = [];

        public Task RegisterSchedulesAsync(IReadOnlyList<RecurringTaskSchedule> schedules, CancellationToken cancellationToken = default)
        {
            this.Registered.Add(schedules);

            return Task.CompletedTask;
        }
    }
}

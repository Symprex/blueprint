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
/// Proves that a schedule collected by <see cref="RecurringTaskManager"/> is registered with Hangfire under legacy
/// Blueprint's id, <c>{SchedulerType}:{Name}</c>, so the job legacy registered for it is the same recurring job, updated in
/// place, and two schedulers may declare the same name.
/// </summary>
/// <remarks>
/// <para>
/// Legacy's <c>RecurringTaskManager</c> built each id from the declaring scheduler's <c>Type.Name</c> — not its full
/// name — and the schedule's name, joined by a colon (Signature365 <c>8b40ecc961</c>,
/// <c>Blueprint.Tasks/RecurringTaskManager.cs:18</c>, <c>:70-73</c>, <c>:123-130</c>), and its Hangfire provider registered
/// the schedule under that id (<c>Blueprint.Tasks.Hangfire/HangfireRecurringTaskProvider.cs:85-86</c>). A nested scheduler,
/// such as Signature365's <c>DeleteUiTestTenantsBackgroundTask.Scheduler</c>, is therefore <c>Scheduler:{Name}</c>.
/// </para>
/// <para>
/// Hangfire keeps a recurring job in the hash <c>recurring-job:{id}</c>, and <c>AddOrUpdate</c> rewrites its <c>Job</c>
/// field every time (Hangfire 1.8.18 <c>RecurringJobManager.AddOrUpdate</c>, decompiled, <c>:101-103</c>, written when it
/// differs, <c>RecurringJobEntity.GetChangedFields</c> <c>:191-194</c>), so registering a legacy job's id replaces the
/// legacy job's executor with the new one rather than adding a second job beside it.
/// </para>
/// </remarks>
public class HangfireRecurringTaskIdTests
{
    private const string AppQueue = "portal";

    [Fact]
    public async Task Each_schedule_is_registered_under_its_schedulers_type_name_and_its_own_name()
    {
        var (manager, calls) = CreateManager();
        var provider = CreateProvider(manager, CreateStorage());

        await new RecurringTaskManager(
                [new NightlyScheduler("cleanup"), new DeleteUiTestTenantsTask.Scheduler("cleanup")],
                provider,
                NullLogger<RecurringTaskManager>.Instance)
            .RescheduleAllAsync(CancellationToken.None);

        calls.Should().BeEquivalentTo("NightlyScheduler:cleanup", "Scheduler:cleanup", "portal:Scheduler");
    }

    [Fact]
    public async Task A_legacy_job_whose_schedule_is_still_declared_is_registered_under_its_own_id_and_not_removed()
    {
        // The job legacy left in storage for NightlyScheduler's "cleanup", under legacy's id.
        var (manager, calls) = CreateManager();
        var provider = CreateProvider(manager, CreateStorage("NightlyScheduler:cleanup"));

        await new RecurringTaskManager([new NightlyScheduler("cleanup")], provider, NullLogger<RecurringTaskManager>.Instance)
            .RescheduleAllAsync(CancellationToken.None);

        calls.Should().Contain("NightlyScheduler:cleanup");
        manager.DidNotReceive().RemoveIfExists("NightlyScheduler:cleanup");
    }

    [Fact]
    public async Task The_schedule_keeps_its_declared_name_and_carries_the_id_separately()
    {
        // Only the id is qualified, so a provider that registers by name sees what the scheduler declared.
        var recording = new RecordingProvider();

        await new RecurringTaskManager([new NightlyScheduler("cleanup")], recording, NullLogger<RecurringTaskManager>.Instance)
            .RescheduleAllAsync(CancellationToken.None);

        var schedule = recording.Registered.Should().ContainSingle().Subject;
        schedule.Name.Should().Be("cleanup");
        schedule.Id.Should().Be("NightlyScheduler:cleanup");
    }

    [Fact]
    public void A_schedule_no_manager_collected_has_its_name_as_its_id()
    {
        var schedule = new RecurringTaskSchedule("cleanup", new IdTestTask(), "0 2 * * *");

        schedule.Id.Should().Be("cleanup");
    }

    private static HangfireRecurringTaskProvider CreateProvider(IRecurringJobManager manager, JobStorage storage)
    {
        return new HangfireRecurringTaskProvider(
            manager,
            storage,
            Options.Create(new BlueprintTaskOptions { DefaultQueue = AppQueue }),
            NullLogger<HangfireRecurringTaskProvider>.Instance);
    }

    private static (IRecurringJobManager Manager, List<string> Calls) CreateManager()
    {
        var calls = new List<string>();
        var manager = Substitute.For<IRecurringJobManager>();
        manager
            .When(static m => m.AddOrUpdate(Arg.Any<string>(), Arg.Any<Job>(), Arg.Any<string>(), Arg.Any<RecurringJobOptions>()))
            .Do(call => calls.Add(call.ArgAt<string>(0)));

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

    public sealed class IdTestTask : IBackgroundTask
    {
        public static Blueprint.Model.ApiOperationDescriptor Configure()
        {
            return new(typeof(IdTestTask), nameof(IdTestTask), "test");
        }
    }

    private sealed class NightlyScheduler(string name) : IRecurringTaskScheduler
    {
        public Task<IEnumerable<RecurringTaskSchedule>> GetTaskSchedulesAsync()
        {
            return Task.FromResult<IEnumerable<RecurringTaskSchedule>>([new RecurringTaskSchedule(name, new IdTestTask(), "0 2 * * *")]);
        }
    }

    /// <summary>Stands in for a task that declares its own scheduler as a nested type, as Signature365's do.</summary>
    private static class DeleteUiTestTenantsTask
    {
        public sealed class Scheduler(string name) : IRecurringTaskScheduler
        {
            public Task<IEnumerable<RecurringTaskSchedule>> GetTaskSchedulesAsync()
            {
                return Task.FromResult<IEnumerable<RecurringTaskSchedule>>([new RecurringTaskSchedule(name, new IdTestTask(), "0 3 * * *")]);
            }
        }
    }

    private sealed class RecordingProvider : IRecurringTaskProvider
    {
        public List<RecurringTaskSchedule> Registered { get; } = [];

        public Task RegisterSchedulesAsync(IReadOnlyList<RecurringTaskSchedule> schedules, CancellationToken cancellationToken = default)
        {
            this.Registered.AddRange(schedules);

            return Task.CompletedTask;
        }
    }
}

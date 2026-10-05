using Blueprint.Tasks;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Blueprint.Hangfire.Tests;

/// <summary>
/// Proves that <see cref="HangfireRecurringTaskProvider.RegisterSchedulesAsync"/> removes every recurring job its storage
/// lists that the current schedules do not declare, except the provider's own scheduler job, as legacy Blueprint's
/// provider did.
/// </summary>
/// <remarks>
/// <para>
/// Legacy's <c>UpdateAsync</c> read every recurring job from storage and removed each one whose id the current set did
/// not contain, excepting only <c>{DefaultQueue}:Scheduler</c> (Signature365 <c>8b40ecc961</c>,
/// <c>Blueprint.Tasks.Hangfire/HangfireRecurringTaskProvider.cs:52-66</c>). What scoped that to one app was the storage:
/// Signature365's <c>RecurringJobPartitionedSqlServerStorage</c> rewrites Hangfire's recurring job set to the app's own.
/// </para>
/// <para>
/// That scope is what removes the jobs of a schedule deleted from code, of a scheduler that now declares nothing, and of
/// a scheduler that no longer exists, whatever their ids: legacy-format jobs no schedule re-declares, and jobs an earlier
/// release of this provider created under <c>blueprint:</c> ids. Without removal they would keep firing indefinitely.
/// </para>
/// </remarks>
public class HangfireRecurringTaskProviderStaleJobTests
{
    [Fact]
    public async Task A_job_no_longer_declared_is_removed_and_a_declared_one_is_kept()
    {
        var (manager, storage) = CreateHangfire("NightlyScheduler:kept", "NightlyScheduler:removed-from-code");
        var sut = CreateProvider(manager, storage);

        await sut.RegisterSchedulesAsync([Schedule("NightlyScheduler:kept")]);

        manager.Received(1).RemoveIfExists("NightlyScheduler:removed-from-code");
        manager.DidNotReceive().RemoveIfExists("NightlyScheduler:kept");
    }

    [Fact]
    public async Task Every_undeclared_job_the_storage_lists_is_removed_whatever_its_id_except_the_scheduler_job()
    {
        // Legacy removed every listed id the set did not declare, bar its scheduler job (legacy :57-58): a job of a scheduler
        // that no longer exists, another code's job, and one an earlier release of this provider created.
        var (manager, storage) = CreateHangfire(
            "DeletedScheduler:cleanup",
            "manual-report",
            "blueprint:pre-release",
            "portal:Scheduler");
        var sut = CreateProvider(manager, storage);

        await sut.RegisterSchedulesAsync([]);

        manager.Received(1).RemoveIfExists("DeletedScheduler:cleanup");
        manager.Received(1).RemoveIfExists("manual-report");
        manager.Received(1).RemoveIfExists("blueprint:pre-release");
        manager.DidNotReceive().RemoveIfExists("portal:Scheduler");
    }

    [Fact]
    public async Task With_no_default_queue_the_legacy_colon_Scheduler_job_is_the_one_kept()
    {
        // Legacy's scheduler id with no default queue (legacy HangfireRecurringTaskProvider.cs:45, :58).
        var (manager, storage) = CreateHangfire(":Scheduler", "default:Scheduler");
        var sut = new HangfireRecurringTaskProvider(
            manager,
            storage,
            Options.Create(new BlueprintTaskOptions()),
            NullLogger<HangfireRecurringTaskProvider>.Instance);

        await sut.RegisterSchedulesAsync([]);

        manager.DidNotReceive().RemoveIfExists(":Scheduler");
        manager.Received(1).RemoveIfExists("default:Scheduler");
    }

    [Fact]
    public async Task A_declared_schedule_whose_stored_id_differs_only_in_case_is_kept()
    {
        // Hangfire's SQL storage compares ids case-insensitively, so this is the same job as the one just registered.
        // Legacy compared ordinally (legacy :58); matching as the storage does is the deliberate difference.
        var (manager, storage) = CreateHangfire("NightlyScheduler:Nightly-Cleanup");
        var sut = CreateProvider(manager, storage);

        await sut.RegisterSchedulesAsync([Schedule("NightlyScheduler:nightly-cleanup")]);

        manager.DidNotReceive().RemoveIfExists(Arg.Any<string>());
    }

    [Fact]
    public async Task Stale_jobs_are_removed_only_after_every_declared_schedule_has_been_registered()
    {
        var (manager, storage) = CreateHangfire("NightlyScheduler:stale");
        manager
            .When(static m => m.AddOrUpdate("NightlyScheduler:second", Arg.Any<Job>(), Arg.Any<string>(), Arg.Any<RecurringJobOptions>()))
            .Do(static _ => throw new InvalidOperationException("The storage rejected the job."));
        var sut = CreateProvider(manager, storage);

        var register = () => sut.RegisterSchedulesAsync([Schedule("NightlyScheduler:first"), Schedule("NightlyScheduler:second")]);

        await register.Should().ThrowAsync<InvalidOperationException>();
        manager.DidNotReceive().RemoveIfExists(Arg.Any<string>());
    }

    [Fact]
    public async Task When_the_storage_cannot_be_read_then_registration_still_succeeds_and_the_failure_is_logged()
    {
        var manager = Substitute.For<IRecurringJobManager>();
        var storage = Substitute.For<JobStorage>();
        storage.GetConnection().Throws(new InvalidOperationException("The storage is unavailable."));
        var logger = new RecordingLogger();
        var sut = new HangfireRecurringTaskProvider(manager, storage, Options.Create(new BlueprintTaskOptions()), logger);

        var register = () => sut.RegisterSchedulesAsync([Schedule("NightlyScheduler:kept")]);

        await register.Should().NotThrowAsync("the declared schedules are registered whether or not stale ones can be found");
        manager.Received(1).AddOrUpdate("NightlyScheduler:kept", Arg.Any<Job>(), Arg.Any<string>(), Arg.Any<RecurringJobOptions>());
        logger.Entries.Should().Contain(static e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task Provider_resolved_through_AddBlueprintHangfire_reads_the_registered_job_storage()
    {
        var (manager, storage) = CreateHangfire("NightlyScheduler:removed-from-code");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(manager);
        services.AddSingleton(storage);
        services.AddBlueprintHangfire();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sut = scope.ServiceProvider.GetRequiredService<IRecurringTaskProvider>();

        await sut.RegisterSchedulesAsync([Schedule("NightlyScheduler:kept")]);

        manager.Received(1).RemoveIfExists("NightlyScheduler:removed-from-code");
    }

    private static HangfireRecurringTaskProvider CreateProvider(IRecurringJobManager manager, JobStorage storage)
    {
        return new HangfireRecurringTaskProvider(
            manager,
            storage,
            Options.Create(new BlueprintTaskOptions { DefaultQueue = "portal" }),
            NullLogger<HangfireRecurringTaskProvider>.Instance);
    }

    private static (IRecurringJobManager Manager, JobStorage Storage) CreateHangfire(params string[] existingRecurringJobIds)
    {
        var connection = Substitute.For<IStorageConnection>();
        connection.GetAllItemsFromSet("recurring-jobs").Returns(_ => new HashSet<string>(existingRecurringJobIds));

        var storage = Substitute.For<JobStorage>();
        storage.GetConnection().Returns(connection);

        return (Substitute.For<IRecurringJobManager>(), storage);
    }

    /// <summary>
    /// A schedule registered directly with the provider, whose id is its name; <see cref="Runtime.Tasks.RecurringTaskManager"/>
    /// would have made it <c>{SchedulerType}:{Name}</c>, which these names stand in for.
    /// </summary>
    private static RecurringTaskSchedule Schedule(string name)
    {
        return new RecurringTaskSchedule(name, new RecurringTestTask(), "0 2 * * *");
    }

    public sealed class RecurringTestTask : IBackgroundTask
    {
        public static Blueprint.Model.ApiOperationDescriptor Configure()
        {
            return new(typeof(RecurringTestTask), nameof(RecurringTestTask), "test");
        }
    }

    private sealed class RecordingLogger : ILogger<HangfireRecurringTaskProvider>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            this.Entries.Add((logLevel, exception));
        }
    }
}

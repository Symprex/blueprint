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
/// Proves that <see cref="HangfireRecurringTaskProvider.RegisterSchedulesAsync"/> removes the recurring jobs it created
/// for schedules that are no longer declared, and never touches a recurring job it did not create.
/// </summary>
/// <remarks>
/// <para>
/// Legacy Blueprint's recurring task provider removed every recurring job its storage listed that the current schedules
/// did not declare, except its own re-scheduling job. It was scoped to one app by the storage: Signature365 partitions
/// Hangfire's recurring job set by app, so each app lists only its own. This provider keeps that storage scoping and
/// narrows it further to the ids it creates, <c>blueprint:{schedule name}</c>, so a recurring job some other code
/// registered in the same storage survives.
/// </para>
/// <para>
/// Without removal, a schedule deleted from code keeps firing in Hangfire indefinitely.
/// </para>
/// </remarks>
public class HangfireRecurringTaskProviderStaleJobTests
{
    [Fact]
    public async Task A_blueprint_job_no_longer_declared_is_removed_and_a_declared_one_is_kept()
    {
        var (manager, storage) = CreateHangfire("blueprint:kept", "blueprint:removed-from-code");
        var sut = CreateProvider(manager, storage);

        await sut.RegisterSchedulesAsync([Schedule("kept")]);

        manager.Received(1).RemoveIfExists("blueprint:removed-from-code");
        manager.DidNotReceive().RemoveIfExists("blueprint:kept");
    }

    [Fact]
    public async Task When_no_schedules_are_declared_then_every_job_this_provider_created_is_removed()
    {
        // The app's last schedule deleted from code: the registration service hands over the empty set.
        var (manager, storage) = CreateHangfire("blueprint:last-one", "blueprint:another", "manual-report");
        var sut = CreateProvider(manager, storage);

        await sut.RegisterSchedulesAsync([]);

        manager.Received(1).RemoveIfExists("blueprint:last-one");
        manager.Received(1).RemoveIfExists("blueprint:another");
        manager.DidNotReceive().RemoveIfExists("manual-report");
    }

    [Fact]
    public async Task A_recurring_job_this_provider_did_not_create_is_never_removed()
    {
        // Legacy's own ids ({SchedulerType}:{Name}) and anything else registered in the same storage.
        var (manager, storage) = CreateHangfire("NightlyScheduler:cleanup", "manual-report", "Blueprint:different-case-prefix");
        var sut = CreateProvider(manager, storage);

        await sut.RegisterSchedulesAsync([Schedule("kept")]);

        manager.DidNotReceive().RemoveIfExists(Arg.Any<string>());
    }

    [Fact]
    public async Task A_declared_schedule_whose_stored_id_differs_only_in_case_is_kept()
    {
        // Hangfire's SQL storage compares ids case-insensitively, so this is the same job as the one just registered.
        var (manager, storage) = CreateHangfire("blueprint:Nightly-Cleanup");
        var sut = CreateProvider(manager, storage);

        await sut.RegisterSchedulesAsync([Schedule("nightly-cleanup")]);

        manager.DidNotReceive().RemoveIfExists(Arg.Any<string>());
    }

    [Fact]
    public async Task Stale_jobs_are_removed_only_after_every_declared_schedule_has_been_registered()
    {
        var (manager, storage) = CreateHangfire("blueprint:stale");
        manager
            .When(static m => m.AddOrUpdate("blueprint:second", Arg.Any<Job>(), Arg.Any<string>(), Arg.Any<RecurringJobOptions>()))
            .Do(static _ => throw new InvalidOperationException("The storage rejected the job."));
        var sut = CreateProvider(manager, storage);

        var register = () => sut.RegisterSchedulesAsync([Schedule("first"), Schedule("second")]);

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

        var register = () => sut.RegisterSchedulesAsync([Schedule("kept")]);

        await register.Should().NotThrowAsync("the declared schedules are registered whether or not stale ones can be found");
        manager.Received(1).AddOrUpdate("blueprint:kept", Arg.Any<Job>(), Arg.Any<string>(), Arg.Any<RecurringJobOptions>());
        logger.Entries.Should().Contain(static e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task Provider_resolved_through_AddBlueprintHangfire_reads_the_registered_job_storage()
    {
        var (manager, storage) = CreateHangfire("blueprint:removed-from-code");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(manager);
        services.AddSingleton(storage);
        services.AddBlueprintHangfire();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sut = scope.ServiceProvider.GetRequiredService<IRecurringTaskProvider>();

        await sut.RegisterSchedulesAsync([Schedule("kept")]);

        manager.Received(1).RemoveIfExists("blueprint:removed-from-code");
    }

    private static HangfireRecurringTaskProvider CreateProvider(IRecurringJobManager manager, JobStorage storage)
    {
        return new HangfireRecurringTaskProvider(
            manager,
            storage,
            Options.Create(new BlueprintTaskOptions()),
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

using Blueprint.Tasks;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Blueprint.Hangfire.Tests;

/// <summary>
/// Proves the queue every Hangfire job Blueprint creates is routed to: the task type's <see cref="Blueprint.Tasks.QueueAttribute"/>, else
/// the app's <see cref="BlueprintTaskOptions.DefaultQueue"/>, for enqueued, scheduled and continuation jobs alike, as
/// legacy Blueprint's schedule provider routed them.
/// </summary>
/// <remarks>
/// A server processes only the queues it listens to, and Signature365's listen to their own app's queue, so a job
/// routed to Hangfire's <c>default</c> queue instead is never run. A scheduled or continuation job with no queue of its
/// own lands on <c>default</c> when it becomes due.
/// </remarks>
public class HangfireQueueRoutingTests
{
    private const string AppQueue = "portal";

    private const string ReportsQueue = "reports";

    [Fact]
    public async Task Enqueued_job_without_a_queue_attribute_goes_to_the_apps_default_queue()
    {
        var (sut, captured) = CreateProvider();

        await sut.EnqueueAsync(new BackgroundTaskEnvelope(new UnroutedTask()));

        captured.Job!.Queue.Should().Be(AppQueue);
        captured.State.Should().BeOfType<EnqueuedState>().Which.Queue.Should().Be(AppQueue);
    }

    [Fact]
    public async Task Enqueued_job_with_a_queue_attribute_goes_to_that_queue()
    {
        var (sut, captured) = CreateProvider();

        await sut.EnqueueAsync(new BackgroundTaskEnvelope(new RoutedTask()));

        captured.Job!.Queue.Should().Be(ReportsQueue);
        captured.State.Should().BeOfType<EnqueuedState>().Which.Queue.Should().Be(ReportsQueue);
    }

    [Fact]
    public async Task Scheduled_job_is_created_on_the_apps_default_queue()
    {
        var (sut, captured) = CreateProvider();
        var before = DateTime.UtcNow;

        await sut.ScheduleAsync(new BackgroundTaskEnvelope(new UnroutedTask()), TimeSpan.FromMinutes(5));

        captured.Job!.Queue.Should().Be(AppQueue, "the job is enqueued to its own queue when it becomes due");
        captured.State.Should().BeOfType<ScheduledState>()
            .Which.EnqueueAt.Should().BeOnOrAfter(before.AddMinutes(5)).And.BeCloseTo(before.AddMinutes(5), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Scheduled_job_with_a_queue_attribute_is_created_on_that_queue()
    {
        var (sut, captured) = CreateProvider();

        await sut.ScheduleAsync(new BackgroundTaskEnvelope(new RoutedTask()), TimeSpan.FromMinutes(5));

        captured.Job!.Queue.Should().Be(ReportsQueue);
    }

    [Theory]
    [InlineData(BackgroundTaskContinuationOptions.OnlyOnSucceededState, JobContinuationOptions.OnlyOnSucceededState)]
    [InlineData(BackgroundTaskContinuationOptions.OnAnyFinishedState, JobContinuationOptions.OnAnyFinishedState)]
    public async Task Continuation_job_awaits_its_parent_and_is_then_enqueued_to_the_apps_default_queue(
        BackgroundTaskContinuationOptions continuationOptions,
        JobContinuationOptions expectedOptions)
    {
        var (sut, captured) = CreateProvider();

        await sut.EnqueueChildAsync(new BackgroundTaskEnvelope(new UnroutedTask()), "parent-1", continuationOptions);

        captured.Job!.Queue.Should().Be(AppQueue);

        var awaiting = captured.State.Should().BeOfType<AwaitingState>().Subject;
        awaiting.ParentId.Should().Be("parent-1");
        awaiting.Options.Should().Be(expectedOptions);
        awaiting.NextState.Should().BeOfType<EnqueuedState>().Which.Queue.Should().Be(AppQueue);
    }

    [Fact]
    public async Task Continuation_job_with_a_queue_attribute_is_enqueued_to_that_queue()
    {
        var (sut, captured) = CreateProvider();

        await sut.EnqueueChildAsync(
            new BackgroundTaskEnvelope(new RoutedTask()),
            "parent-1",
            BackgroundTaskContinuationOptions.OnlyOnSucceededState);

        captured.Job!.Queue.Should().Be(ReportsQueue);
        captured.State.Should().BeOfType<AwaitingState>()
            .Which.NextState.Should().BeOfType<EnqueuedState>().Which.Queue.Should().Be(ReportsQueue);
    }

    [Fact]
    public async Task Recurring_job_without_a_queue_goes_to_the_apps_default_queue()
    {
        var manager = Substitute.For<IRecurringJobManager>();
        Job? job = null;
        manager.AddOrUpdate(Arg.Any<string>(), Arg.Do<Job>(j => job = j), Arg.Any<string>(), Arg.Any<RecurringJobOptions>());

        var sut = new HangfireRecurringTaskProvider(
            manager,
            Options.Create(new BlueprintTaskOptions { DefaultQueue = AppQueue }),
            NullLogger<HangfireRecurringTaskProvider>.Instance);

        await sut.RegisterSchedulesAsync([new RecurringTaskSchedule("nightly", new UnroutedTask(), "0 2 * * *")]);

        job!.Queue.Should().Be(AppQueue);
    }

    [Fact]
    public async Task Recurring_job_for_a_task_with_a_queue_attribute_still_goes_to_the_apps_default_queue()
    {
        // Legacy routed a recurring job by its schedule's queue, else the default queue, and never read the task's
        // [Queue] (legacy Blueprint.Tasks.Hangfire/HangfireRecurringTaskProvider.cs:76 at Signature365 8b40ecc961); only
        // its enqueue path did (HangfireBackgroundTaskScheduleProvider.cs:80-93).
        var job = await RegisterRecurringAsync(new RecurringTaskSchedule("nightly", new RoutedTask(), "0 2 * * *"));

        job.Queue.Should().Be(AppQueue);
    }

    [Fact]
    public async Task Recurring_job_whose_schedule_names_a_queue_goes_to_that_queue()
    {
        // The schedule's own queue comes first (legacy HangfireRecurringTaskProvider.cs:76).
        var job = await RegisterRecurringAsync(
            new RecurringTaskSchedule("nightly", new RoutedTask(), "0 2 * * *", TimeZoneInfo.Utc, queue: "nightly-jobs"));

        job.Queue.Should().Be("nightly-jobs");
    }

    [Fact]
    public async Task Provider_resolved_through_AddBlueprintHangfire_uses_the_configured_default_queue()
    {
        var captured = new CapturedJob();
        var services = new ServiceCollection();
        services.AddSingleton(CreateClient(captured));
        services.Configure<BlueprintTaskOptions>(static o => o.DefaultQueue = AppQueue);
        services.AddBlueprintHangfire();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sut = scope.ServiceProvider.GetRequiredService<IBackgroundTaskScheduleProvider>();

        await sut.EnqueueAsync(new BackgroundTaskEnvelope(new UnroutedTask()));

        captured.Job!.Queue.Should().Be(AppQueue, "the container must construct the provider with the app's task options");
    }

    private static async Task<Job> RegisterRecurringAsync(RecurringTaskSchedule schedule)
    {
        var manager = Substitute.For<IRecurringJobManager>();
        var jobs = new Dictionary<string, Job>();
        manager
            .When(static m => m.AddOrUpdate(Arg.Any<string>(), Arg.Any<Job>(), Arg.Any<string>(), Arg.Any<RecurringJobOptions>()))
            .Do(call => jobs[call.ArgAt<string>(0)] = call.ArgAt<Job>(1));

        var sut = new HangfireRecurringTaskProvider(
            manager,
            Options.Create(new BlueprintTaskOptions { DefaultQueue = AppQueue }),
            NullLogger<HangfireRecurringTaskProvider>.Instance);

        await sut.RegisterSchedulesAsync([schedule]);

        return jobs.Single(static pair => !pair.Key.EndsWith(":Scheduler", StringComparison.Ordinal)).Value;
    }

    private static IBackgroundJobClient CreateClient(CapturedJob captured)
    {
        var client = Substitute.For<IBackgroundJobClient>();
        client.Create(Arg.Any<Job>(), Arg.Any<IState>())
            .Returns(call =>
            {
                captured.Job = call.ArgAt<Job>(0);
                captured.State = call.ArgAt<IState>(1);

                return "job-1";
            });

        return client;
    }

    private static (HangfireBackgroundTaskScheduleProvider Provider, CapturedJob Captured) CreateProvider()
    {
        var captured = new CapturedJob();
        var provider = new HangfireBackgroundTaskScheduleProvider(
            CreateClient(captured),
            Options.Create(new BlueprintTaskOptions { DefaultQueue = AppQueue }));

        return (provider, captured);
    }

    private sealed class CapturedJob
    {
        public Job? Job { get; set; }

        public IState? State { get; set; }
    }

    public sealed class UnroutedTask : IBackgroundTask
    {
        public static Blueprint.Model.ApiOperationDescriptor Configure()
        {
            return new(typeof(UnroutedTask), nameof(UnroutedTask), "test");
        }
    }

    [Blueprint.Tasks.Queue(ReportsQueue)]
    public sealed class RoutedTask : IBackgroundTask
    {
        public static Blueprint.Model.ApiOperationDescriptor Configure()
        {
            return new(typeof(RoutedTask), nameof(RoutedTask), "test");
        }
    }
}

using Blueprint.Runtime.Tasks;
using Blueprint.Tasks;
using Hangfire;
using Hangfire.AspNetCore;
using Hangfire.Common;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Blueprint.Hangfire.Tests;

/// <summary>
/// Pins where the <c>{DefaultQueue}:Scheduler</c> job's <see cref="RecurringTaskManager"/> comes from when it runs: the
/// container of whichever process's Hangfire server took the job from its queue, whether or not that process called
/// <c>AddRecurringTaskProvider</c> or registered any scheduler.
/// </summary>
/// <remarks>
/// <para>
/// Hangfire.NetCore's job activator resolves a job's type with
/// <c>ActivatorUtilities.GetServiceOrCreateInstance</c> (1.8.18, <c>AspNetCoreJobActivatorScope.Resolve</c>, as decompiled),
/// so it builds a <see cref="RecurringTaskManager"/> that was never registered from whatever the process has: here
/// <c>AddBlueprintHangfire</c>'s provider, and no scheduler. The manager then hands the provider an empty set, and the
/// provider removes every recurring job its storage lists but the scheduler job, as legacy's did
/// (Signature365 <c>8b40ecc961</c>, <c>Blueprint.Tasks.Hangfire/HangfireRecurringTaskProvider.cs:52-66</c>).
/// </para>
/// <para>
/// So it is the queues a process's server listens to that decide where registration runs, not where the provider is
/// registered: every process whose server listens to the default queue must register the provider and every scheduler.
/// </para>
/// </remarks>
public class HangfireSchedulerJobActivationTests
{
    [Fact]
    public async Task A_process_with_the_provider_but_no_schedulers_that_runs_the_scheduler_job_removes_every_recurring_job()
    {
        var manager = Substitute.For<IRecurringJobManager>();
        var storage = CreateStorage("NightlyScheduler:cleanup", "Microsoft365RecurringTaskScheduler:import-tenant-1", "portal:Scheduler");

        // A worker listening to the app's default queue, with Blueprint.Hangfire registered but not AddRecurringTaskProvider,
        // and none of the app's schedulers.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(manager);
        services.AddSingleton(storage);
        services.AddSingleton(Options.Create(new BlueprintTaskOptions { DefaultQueue = "portal" }));
        services.AddBlueprintHangfire();

        await using var provider = services.BuildServiceProvider();
        var activator = new AspNetCoreJobActivator(provider.GetRequiredService<IServiceScopeFactory>());

        // What Hangfire does when the scheduler job fires: activate its type in a scope, then invoke its method.
        var job = Job.FromExpression<RecurringTaskManager>(static m => m.RescheduleAllAsync(CancellationToken.None));
        using var scope = activator.BeginScope(
            new JobActivatorContext(
                Substitute.For<IStorageConnection>(),
                new BackgroundJob("job-1", job, DateTime.UtcNow),
                Substitute.For<IJobCancellationToken>()));

        var target = (RecurringTaskManager)scope.Resolve(typeof(RecurringTaskManager));
        await target.RescheduleAllAsync(CancellationToken.None);

        manager.Received(1).RemoveIfExists("NightlyScheduler:cleanup");
        manager.Received(1).RemoveIfExists("Microsoft365RecurringTaskScheduler:import-tenant-1");
        manager.DidNotReceive().RemoveIfExists("portal:Scheduler");
    }

    private static JobStorage CreateStorage(params string[] existingRecurringJobIds)
    {
        var connection = Substitute.For<IStorageConnection>();
        connection.GetAllItemsFromSet("recurring-jobs").Returns(_ => new HashSet<string>(existingRecurringJobIds));

        var storage = Substitute.For<JobStorage>();
        storage.GetConnection().Returns(connection);

        return storage;
    }
}

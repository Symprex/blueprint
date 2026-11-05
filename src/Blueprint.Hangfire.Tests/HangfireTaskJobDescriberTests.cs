using Blueprint.Tasks;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Blueprint.Hangfire.Tests;

/// <summary>
/// Proves a job Blueprint created can be named after the task it carries from its stored invocation data alone, without
/// loading the task's type, as a monitoring app that does not reference the task's assembly reads it.
/// </summary>
/// <remarks>
/// Signature365's Admin Portal used to recognise only legacy Blueprint's <c>HangfireTaskExecutor.Execute</c> jobs, and
/// named every job this package wrote after its method, <c>ExecuteAsync</c>, because the payload format was internal.
/// </remarks>
public class HangfireTaskJobDescriberTests
{
    [Fact]
    public async Task Enqueued_job_is_described_by_the_task_it_carries()
    {
        var job = await CaptureEnqueuedJobAsync(new DescribedTask { TenantId = "ten_1" });

        var described = HangfireTaskJobDescriber.TryDescribe(InvocationData.SerializeJob(job), out var description);

        described.Should().BeTrue();
        description!.Name.Should().Be("DescribedTask");
        description.TaskTypeName.Should().Be(typeof(DescribedTask).AssemblyQualifiedName);
        description.TaskJson.Should().Be("""{"TenantId":"ten_1"}""");
    }

    [Fact]
    public void Job_as_storage_holds_it_is_described_without_loading_the_task_type()
    {
        // The stored columns as Hangfire writes them for this package's executor: each argument serialised on its own,
        // then the array of them. The task type names an assembly that does not exist, so nothing can load it.
        var invocationData = new InvocationData(
            "Blueprint.Hangfire.HangfireBackgroundTaskExecutor, Blueprint.Hangfire, Version=2.2.0.0, Culture=neutral, PublicKeyToken=null",
            "ExecuteAsync",
            """["System.String, System.Private.CoreLib","System.Threading.CancellationToken, System.Private.CoreLib"]""",
            """["\"{\\\"TaskTypeName\\\":\\\"Symprex.Signature365.Portal.Domain.Microsoft365.ImportEntraIdBackgroundTask, Symprex.Signature365.Portal\\\",\\\"TaskJson\\\":\\\"{\\\\\\\"TenantId\\\\\\\":\\\\\\\"ten_pws9ld7w5poEvhrH\\\\\\\"}\\\",\\\"Headers\\\":{\\\"traceparent\\\":\\\"abc\\\"}}\"",null]""",
            "portal");

        var described = HangfireTaskJobDescriber.TryDescribe(invocationData, out var description);

        described.Should().BeTrue();
        description!.Name.Should().Be("ImportEntraIdBackgroundTask");
        description.TaskTypeName.Should().Be("Symprex.Signature365.Portal.Domain.Microsoft365.ImportEntraIdBackgroundTask, Symprex.Signature365.Portal");
        description.TaskJson.Should().Be("""{"TenantId":"ten_pws9ld7w5poEvhrH"}""");
    }

    [Fact]
    public async Task Closed_generic_task_is_named_without_its_generic_arguments()
    {
        var job = await CaptureEnqueuedJobAsync(new GenericTask<DescribedTask>());

        HangfireTaskJobDescriber.TryDescribe(InvocationData.SerializeJob(job), out var description).Should().BeTrue();
        description!.Name.Should().Be("GenericTask`1");
        description.TaskTypeName.Should().Be(typeof(GenericTask<DescribedTask>).AssemblyQualifiedName);
    }

    [Fact]
    public async Task Recurring_job_is_described_by_the_task_it_carries()
    {
        var manager = Substitute.For<IRecurringJobManager>();
        var jobs = new Dictionary<string, Job>();
        manager
            .When(static m => m.AddOrUpdate(Arg.Any<string>(), Arg.Any<Job>(), Arg.Any<string>(), Arg.Any<RecurringJobOptions>()))
            .Do(call => jobs[call.ArgAt<string>(0)] = call.ArgAt<Job>(1));

        var sut = new HangfireRecurringTaskProvider(
            manager,
            Options.Create(new BlueprintTaskOptions()),
            NullLogger<HangfireRecurringTaskProvider>.Instance);

        await sut.RegisterSchedulesAsync([new RecurringTaskSchedule("nightly", new DescribedTask { TenantId = "ten_2" }, "0 2 * * *")]);

        var job = jobs.Single(static pair => !pair.Key.EndsWith(":Scheduler", StringComparison.Ordinal)).Value;

        HangfireTaskJobDescriber.TryDescribe(InvocationData.SerializeJob(job), out var description).Should().BeTrue();
        description!.Name.Should().Be("DescribedTask");
        description.TaskJson.Should().Be("""{"TenantId":"ten_2"}""");
    }

    [Fact]
    public void Job_of_another_method_is_not_described()
    {
        // The executor's type and a readable payload, so only the method tells this job apart from one Blueprint created.
        var invocationData = new InvocationData(
            "Blueprint.Hangfire.HangfireBackgroundTaskExecutor, Blueprint.Hangfire",
            "SomeOtherMethod",
            "[]",
            """["\"{\\\"TaskTypeName\\\":\\\"Some.Task, Some\\\",\\\"TaskJson\\\":\\\"{}\\\"}\"",null]""",
            "default");

        HangfireTaskJobDescriber.TryDescribe(invocationData, out var description).Should().BeFalse();
        description.Should().BeNull();
    }

    [Fact]
    public void Job_of_another_types_ExecuteAsync_is_not_described()
    {
        var invocationData = new InvocationData(
            "Blueprint.Hangfire.HangfireBackgroundTaskExecutorProxy, SomeAssembly",
            "ExecuteAsync",
            "[]",
            """["\"{\\\"TaskTypeName\\\":\\\"Some.Task, Some\\\",\\\"TaskJson\\\":\\\"{}\\\"}\"",null]""",
            "default");

        HangfireTaskJobDescriber.TryDescribe(invocationData, out _).Should().BeFalse();
    }

    [Fact]
    public void Job_written_by_legacy_Blueprint_is_not_described()
    {
        // Legacy Blueprint's executor stored a different payload, which the app that still holds such jobs reads itself.
        var invocationData = new InvocationData(
            "Blueprint.Tasks.Hangfire.HangfireTaskExecutor, Blueprint.Tasks.Hangfire, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
            "Execute",
            "[]",
            """["{\"Envelope\":{\"Task\":{\"$type\":\"Some.Task, Some\"}}}",null,null]""",
            "portal");

        HangfireTaskJobDescriber.TryDescribe(invocationData, out _).Should().BeFalse();
    }

    private static async Task<Job> CaptureEnqueuedJobAsync(IBackgroundTask task)
    {
        Job? captured = null;
        var client = Substitute.For<IBackgroundJobClient>();
        client.Create(Arg.Do<Job>(j => captured = j), Arg.Any<IState>()).Returns("job-1");

        await new HangfireBackgroundTaskScheduleProvider(client).EnqueueAsync(new BackgroundTaskEnvelope(task));

        return captured!;
    }

    private sealed class DescribedTask : IBackgroundTask
    {
        public string? TenantId { get; set; }
    }

    private sealed class GenericTask<T> : IBackgroundTask
    {
    }
}

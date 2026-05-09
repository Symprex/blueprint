using System.Diagnostics;
using Blueprint.Runtime.Tasks;
using Blueprint.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Proves the production <see cref="BackgroundTaskScheduler"/> correctly buffers tasks,
/// applies preprocessors, and dispatches to the registered <see cref="IBackgroundTaskScheduleProvider"/>.
/// </summary>
public class BackgroundTaskSchedulerIntegrationTests
{
    [Fact]
    public async Task When_tasks_enqueued_then_RunNow_dispatches_all_to_provider()
    {
        var provider = new TestScheduleProvider();
        var scheduler = CreateScheduler(provider);

        scheduler.Enqueue(new DispatchableBackgroundTask { Payload = "A" });
        scheduler.Enqueue(new DispatchableBackgroundTask { Payload = "B" });

        await scheduler.RunNowAsync();

        provider.EnqueuedEnvelopes.Should().HaveCount(2);
    }

    [Fact]
    public async Task When_task_scheduled_with_delay_then_provider_receives_delay()
    {
        var provider = new TestScheduleProvider();
        var scheduler = CreateScheduler(provider);

        scheduler.Schedule(new DispatchableBackgroundTask { Payload = "delayed" }, TimeSpan.FromMinutes(5));

        await scheduler.RunNowAsync();

        provider.ScheduledEnvelopes.Should().HaveCount(1);
        provider.ScheduledEnvelopes[0].Delay.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task When_RunNow_called_with_no_tasks_then_noop()
    {
        var provider = new TestScheduleProvider();
        var scheduler = CreateScheduler(provider);

        await scheduler.RunNowAsync();

        provider.EnqueuedEnvelopes.Should().BeEmpty();
        provider.ScheduledEnvelopes.Should().BeEmpty();
    }

    [Fact]
    public async Task When_continuation_added_then_child_dispatched_with_parent_id()
    {
        var provider = new TestScheduleProvider();
        var scheduler = CreateScheduler(provider);

        var parent = scheduler.Enqueue(new DispatchableBackgroundTask { Payload = "parent" });
        parent.ContinueWith(new DispatchableBackgroundTask { Payload = "child" });

        await scheduler.RunNowAsync();

        provider.EnqueuedEnvelopes.Should().HaveCount(1);
        provider.ChildEnvelopes.Should().HaveCount(1);
        provider.ChildEnvelopes[0].ParentId.Should().Be("job-0");
    }

    [Fact]
    public async Task When_preprocessor_registered_then_headers_enriched()
    {
        var provider = new TestScheduleProvider();
        var preprocessor = new TestPreprocessor();
        var scheduler = CreateScheduler(provider, preprocessor);

        scheduler.Enqueue(new DispatchableBackgroundTask { Payload = "enriched" });

        await scheduler.RunNowAsync();

        provider.EnqueuedEnvelopes[0].Headers.Should().ContainKey("x-test")
            .WhoseValue.Should().Be("enriched-value");
    }

    [Fact]
    public void TraceContextPreprocessor_injects_traceparent_from_activity()
    {
        using var source = new ActivitySource("test.trace.propagation");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("test-operation");
        activity.Should().NotBeNull("activity source must be sampled");

        var envelope = new BackgroundTaskEnvelope(new DispatchableBackgroundTask { Payload = "traced" });
        var preprocessor = new TraceContextPreprocessor();

        preprocessor.Preprocess(envelope);

        envelope.Headers.Should().ContainKey("traceparent");
        envelope.Headers!["traceparent"].Should().Contain(activity!.TraceId.ToString());
    }

    [Fact]
    public async Task When_RunNow_called_twice_then_second_call_dispatches_nothing()
    {
        var provider = new TestScheduleProvider();
        var scheduler = CreateScheduler(provider);

        scheduler.Enqueue(new DispatchableBackgroundTask { Payload = "once" });

        await scheduler.RunNowAsync();
        await scheduler.RunNowAsync();

        provider.EnqueuedEnvelopes.Should().HaveCount(1, "tasks should only be dispatched once");
    }

    private static BackgroundTaskScheduler CreateScheduler(
        TestScheduleProvider provider,
        IBackgroundTaskPreprocessor? preprocessor = null)
    {
        var preprocessors = preprocessor is not null
            ? new[] { preprocessor }
            : Array.Empty<IBackgroundTaskPreprocessor>();

        return new BackgroundTaskScheduler(
            provider,
            preprocessors,
            NullLogger<BackgroundTaskScheduler>.Instance);
    }

    private sealed class TestPreprocessor : IBackgroundTaskPreprocessor
    {
        public void Preprocess(BackgroundTaskEnvelope envelope)
        {
            envelope.Headers ??= new Dictionary<string, string>();
            envelope.Headers["x-test"] = "enriched-value";
        }
    }

    private sealed class TestScheduleProvider : IBackgroundTaskScheduleProvider
    {
        private int _counter;

        public List<BackgroundTaskEnvelope> EnqueuedEnvelopes { get; } = [];

        public List<(BackgroundTaskEnvelope Envelope, TimeSpan Delay)> ScheduledEnvelopes { get; } = [];

        public List<(BackgroundTaskEnvelope Envelope, string ParentId)> ChildEnvelopes { get; } = [];

        public Task<string> EnqueueAsync(BackgroundTaskEnvelope envelope)
        {
            this.EnqueuedEnvelopes.Add(envelope);
            return Task.FromResult($"job-{this._counter++}");
        }

        public Task<string> ScheduleAsync(BackgroundTaskEnvelope envelope, TimeSpan delay)
        {
            this.ScheduledEnvelopes.Add((envelope, delay));
            return Task.FromResult($"scheduled-{this._counter++}");
        }

        public Task<string> EnqueueChildAsync(
            BackgroundTaskEnvelope envelope,
            string parentId,
            BackgroundTaskContinuationOptions options)
        {
            this.ChildEnvelopes.Add((envelope, parentId));
            return Task.FromResult($"child-{this._counter++}");
        }
    }
}

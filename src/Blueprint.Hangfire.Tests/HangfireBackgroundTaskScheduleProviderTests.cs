using Blueprint.Tasks;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using NSubstitute;

namespace Blueprint.Hangfire.Tests;

/// <summary>
/// Proves that <see cref="HangfireBackgroundTaskScheduleProvider"/> correctly serializes tasks
/// and delegates to the Hangfire <see cref="IBackgroundJobClient"/>.
/// </summary>
public class HangfireBackgroundTaskScheduleProviderTests
{
    [Fact]
    public async Task EnqueueAsync_creates_job_via_hangfire_client()
    {
        var client = Substitute.For<IBackgroundJobClient>();
        client.Create(Arg.Any<Job>(), Arg.Any<IState>())
            .Returns("job-123");

        var sut = new HangfireBackgroundTaskScheduleProvider(client);
        var task = new TestBackgroundTask { Value = "hello" };
        var envelope = new BackgroundTaskEnvelope(task);

        var jobId = await sut.EnqueueAsync(envelope);

        jobId.Should().Be("job-123");
        client.Received(1).Create(Arg.Any<Job>(), Arg.Any<IState>());
    }

    [Fact]
    public async Task ScheduleAsync_creates_delayed_job()
    {
        var client = Substitute.For<IBackgroundJobClient>();
        client.Create(Arg.Any<Job>(), Arg.Any<IState>())
            .Returns("job-456");

        var sut = new HangfireBackgroundTaskScheduleProvider(client);
        var task = new TestBackgroundTask { Value = "delayed" };
        var envelope = new BackgroundTaskEnvelope(task);

        var jobId = await sut.ScheduleAsync(envelope, TimeSpan.FromMinutes(5));

        jobId.Should().Be("job-456");
        client.Received(1).Create(Arg.Any<Job>(), Arg.Any<IState>());
    }

    [Fact]
    public async Task EnqueueChildAsync_creates_continuation_job()
    {
        var client = Substitute.For<IBackgroundJobClient>();
        client.Create(Arg.Any<Job>(), Arg.Any<IState>())
            .Returns("job-789");

        var sut = new HangfireBackgroundTaskScheduleProvider(client);
        var task = new TestBackgroundTask { Value = "child" };
        var envelope = new BackgroundTaskEnvelope(task);

        var jobId = await sut.EnqueueChildAsync(
            envelope,
            "parent-job-1",
            BackgroundTaskContinuationOptions.OnlyOnSucceededState);

        jobId.Should().Be("job-789");
        client.Received(1).Create(Arg.Any<Job>(), Arg.Any<IState>());
    }

    private class TestBackgroundTask : IBackgroundTask
    {
        public string Value { get; set; } = string.Empty;

        public static Blueprint.Model.ApiOperationDescriptor Configure()
        {
            return new(typeof(TestBackgroundTask), "TestBackgroundTask", "test");
        }
    }
}

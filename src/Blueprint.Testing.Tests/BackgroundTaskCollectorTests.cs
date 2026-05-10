using Blueprint.SourceGenerator.Sample.Contract.Api;
using Blueprint.Tasks;
using FluentAssertions;

namespace Blueprint.Testing.Tests;

/// <summary>
/// Tests for <see cref="BackgroundTaskCollector"/> — the test-only task capture provider.
/// </summary>
public class BackgroundTaskCollectorTests
{
    private readonly BackgroundTaskCollector _collector = new();

    [Fact]
    public async Task EnqueueAsync_captures_task_and_returns_id()
    {
        var task = new SendTemplateSharedNotificationTask { TemplateId = "t1", RecipientEmail = "test@example.com" };
        var envelope = new BackgroundTaskEnvelope(task);

        var id = await this._collector.EnqueueAsync(envelope);

        id.Should().StartWith("test-job-");
        this._collector.Enqueued.Should().HaveCount(1);
        this._collector.Enqueued[0].Task.Should().BeSameAs(task);
    }

    [Fact]
    public async Task ScheduleAsync_captures_task_with_delay()
    {
        var task = new SendTemplateSharedNotificationTask { TemplateId = "t2", RecipientEmail = "a@b.com" };
        var envelope = new BackgroundTaskEnvelope(task);
        var delay = TimeSpan.FromMinutes(5);

        await this._collector.ScheduleAsync(envelope, delay);

        this._collector.Scheduled.Should().HaveCount(1);
        this._collector.Scheduled[0].Delay.Should().Be(delay);
        this._collector.Scheduled[0].Envelope.Task.Should().BeSameAs(task);
    }

    [Fact]
    public async Task EnqueueChildAsync_captures_parent_id_and_options()
    {
        var parentTask = new SendTemplateSharedNotificationTask { TemplateId = "parent" };
        var parentEnvelope = new BackgroundTaskEnvelope(parentTask);
        var parentId = await this._collector.EnqueueAsync(parentEnvelope);

        var childTask = new SendTemplateSharedNotificationTask { TemplateId = "child" };
        var childEnvelope = new BackgroundTaskEnvelope(childTask);
        await this._collector.EnqueueChildAsync(childEnvelope, parentId, BackgroundTaskContinuationOptions.OnlyOnSucceededState);

        this._collector.ChildEnqueued.Should().HaveCount(1);
        this._collector.ChildEnqueued[0].ParentId.Should().Be(parentId);
        this._collector.ChildEnqueued[0].Options.Should().Be(BackgroundTaskContinuationOptions.OnlyOnSucceededState);
    }

    [Fact]
    public async Task GetSingleEnqueued_returns_typed_task()
    {
        var task = new SendTemplateSharedNotificationTask { TemplateId = "t3", RecipientEmail = "c@d.com" };
        await this._collector.EnqueueAsync(new BackgroundTaskEnvelope(task));

        var result = this._collector.GetSingleEnqueued<SendTemplateSharedNotificationTask>();

        result.Should().BeSameAs(task);
    }

    [Fact]
    public void GetSingleEnqueued_throws_when_none_enqueued()
    {
        var act = () => this._collector.GetSingleEnqueued<SendTemplateSharedNotificationTask>();

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("none were enqueued");
    }

    [Fact]
    public async Task GetAllEnqueued_returns_all_matching_tasks()
    {
        await this._collector.EnqueueAsync(new BackgroundTaskEnvelope(
            new SendTemplateSharedNotificationTask { TemplateId = "a" }));
        await this._collector.EnqueueAsync(new BackgroundTaskEnvelope(
            new SendTemplateSharedNotificationTask { TemplateId = "b" }));

        var results = this._collector.GetAllEnqueued<SendTemplateSharedNotificationTask>();

        results.Should().HaveCount(2);
    }

    [Fact]
    public void AssertNoneEnqueued_passes_when_empty()
    {
        var act = () => this._collector.AssertNoneEnqueued<SendTemplateSharedNotificationTask>();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task AssertNoneEnqueued_throws_when_tasks_present()
    {
        await this._collector.EnqueueAsync(new BackgroundTaskEnvelope(
            new SendTemplateSharedNotificationTask { TemplateId = "x" }));

        var act = () => this._collector.AssertNoneEnqueued<SendTemplateSharedNotificationTask>();

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("found 1");
    }

    [Fact]
    public async Task ClearAll_resets_all_collections()
    {
        await this._collector.EnqueueAsync(new BackgroundTaskEnvelope(
            new SendTemplateSharedNotificationTask { TemplateId = "clear" }));
        await this._collector.ScheduleAsync(
            new BackgroundTaskEnvelope(new SendTemplateSharedNotificationTask { TemplateId = "clear2" }),
            TimeSpan.FromSeconds(10));

        this._collector.ClearAll();

        this._collector.Enqueued.Should().BeEmpty();
        this._collector.Scheduled.Should().BeEmpty();
        this._collector.ChildEnqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task Each_enqueue_returns_unique_id()
    {
        var ids = new HashSet<string>();

        for (var i = 0; i < 10; i++)
        {
            var id = await this._collector.EnqueueAsync(new BackgroundTaskEnvelope(
                new SendTemplateSharedNotificationTask { TemplateId = $"t{i}" }));
            ids.Add(id);
        }

        ids.Should().HaveCount(10, "each enqueue should produce a unique ID");
    }
}

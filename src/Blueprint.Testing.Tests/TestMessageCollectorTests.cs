using FluentAssertions;

namespace Blueprint.Testing.Tests;

public sealed class TestMessageCollectorTests
{
    private sealed record OrderCreatedEvent(string OrderId, decimal Amount);

    private sealed record PaymentProcessedEvent(string PaymentId);

    private sealed record SendEmailCommand(string To, string Subject);

    [Fact]
    public void Initially_empty()
    {
        var collector = new TestMessageCollector();

        collector.Published.Should().BeEmpty();
        collector.Sent.Should().BeEmpty();
    }

    [Fact]
    public void Publish_captures_message()
    {
        var collector = new TestMessageCollector();
        var msg = new OrderCreatedEvent("ord-1", 100m);

        collector.Publish(msg);

        collector.Published.Should().HaveCount(1);
        collector.Published[0].Message.Should().Be(msg);
        collector.Published[0].MessageType.Should().Be(typeof(OrderCreatedEvent));
    }

    [Fact]
    public void Send_captures_message()
    {
        var collector = new TestMessageCollector();
        var msg = new SendEmailCommand("user@example.com", "Hello");

        collector.Send(msg);

        collector.Sent.Should().HaveCount(1);
        collector.Sent[0].Message.Should().Be(msg);
        collector.Sent[0].MessageType.Should().Be(typeof(SendEmailCommand));
    }

    [Fact]
    public void GetSinglePublished_returns_matching_message()
    {
        var collector = new TestMessageCollector();
        var msg = new OrderCreatedEvent("ord-1", 50m);

        collector.Publish(msg);
        collector.Publish(new PaymentProcessedEvent("pay-1"));

        var result = collector.GetSinglePublished<OrderCreatedEvent>();

        result.Should().Be(msg);
    }

    [Fact]
    public void GetSinglePublished_throws_when_none_found()
    {
        var collector = new TestMessageCollector();
        collector.Publish(new PaymentProcessedEvent("pay-1"));

        var act = () => collector.GetSinglePublished<OrderCreatedEvent>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*OrderCreatedEvent*none were published*");
    }

    [Fact]
    public void GetSinglePublished_throws_when_multiple_found()
    {
        var collector = new TestMessageCollector();
        collector.Publish(new OrderCreatedEvent("ord-1", 10m));
        collector.Publish(new OrderCreatedEvent("ord-2", 20m));

        var act = () => collector.GetSinglePublished<OrderCreatedEvent>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*found 2*");
    }

    [Fact]
    public void GetAllPublished_returns_all_matching()
    {
        var collector = new TestMessageCollector();
        collector.Publish(new OrderCreatedEvent("ord-1", 10m));
        collector.Publish(new PaymentProcessedEvent("pay-1"));
        collector.Publish(new OrderCreatedEvent("ord-2", 20m));

        var results = collector.GetAllPublished<OrderCreatedEvent>();

        results.Should().HaveCount(2);
        results[0].OrderId.Should().Be("ord-1");
        results[1].OrderId.Should().Be("ord-2");
    }

    [Fact]
    public void GetSingleSent_returns_matching_message()
    {
        var collector = new TestMessageCollector();
        var msg = new SendEmailCommand("a@b.com", "Test");

        collector.Send(msg);

        collector.GetSingleSent<SendEmailCommand>().Should().Be(msg);
    }

    [Fact]
    public void GetSingleSent_throws_when_empty()
    {
        var collector = new TestMessageCollector();

        var act = () => collector.GetSingleSent<SendEmailCommand>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SendEmailCommand*none were sent*");
    }

    [Fact]
    public void AssertNonePublished_passes_when_empty()
    {
        var collector = new TestMessageCollector();

        var act = () => collector.AssertNonePublished<OrderCreatedEvent>();

        act.Should().NotThrow();
    }

    [Fact]
    public void AssertNonePublished_throws_when_present()
    {
        var collector = new TestMessageCollector();
        collector.Publish(new OrderCreatedEvent("ord-1", 10m));

        var act = () => collector.AssertNonePublished<OrderCreatedEvent>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no published*OrderCreatedEvent*found 1*");
    }

    [Fact]
    public void AssertNoneSent_passes_when_empty()
    {
        var collector = new TestMessageCollector();

        var act = () => collector.AssertNoneSent<SendEmailCommand>();

        act.Should().NotThrow();
    }

    [Fact]
    public void AssertNoneSent_throws_when_present()
    {
        var collector = new TestMessageCollector();
        collector.Send(new SendEmailCommand("a@b.com", "Hi"));

        var act = () => collector.AssertNoneSent<SendEmailCommand>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no sent*SendEmailCommand*found 1*");
    }

    [Fact]
    public void ClearAll_removes_all_messages()
    {
        var collector = new TestMessageCollector();
        collector.Publish(new OrderCreatedEvent("ord-1", 10m));
        collector.Send(new SendEmailCommand("a@b.com", "Hi"));

        collector.ClearAll();

        collector.Published.Should().BeEmpty();
        collector.Sent.Should().BeEmpty();
    }

    [Fact]
    public void Publish_and_Send_are_independent()
    {
        var collector = new TestMessageCollector();
        collector.Publish(new OrderCreatedEvent("ord-1", 10m));
        collector.Send(new SendEmailCommand("a@b.com", "Hi"));

        collector.Published.Should().HaveCount(1);
        collector.Sent.Should().HaveCount(1);

        collector.Published[0].Message.Should().BeOfType<OrderCreatedEvent>();
        collector.Sent[0].Message.Should().BeOfType<SendEmailCommand>();
    }
}

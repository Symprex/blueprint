using FluentAssertions;
using NSubstitute;
using Rebus.Bus;
using Xunit;

namespace Blueprint.Marten.Rebus.Tests;

/// <summary>
/// Verifies that <see cref="RebusDomainEventPublisher"/> correctly bridges
/// domain events from Marten session listeners to the Rebus bus.
/// </summary>
public class RebusDomainEventPublisherTests
{
    [Fact]
    public async Task PublishAsync_publishes_event_to_rebus_bus()
    {
        var bus = Substitute.For<IBus>();
        var publisher = new RebusDomainEventPublisher(bus);
        var domainEvent = new TestDomainEvent("test-123");

        await publisher.PublishAsync(domainEvent, CancellationToken.None);

        await bus.Received(1).Publish(domainEvent);
    }

    [Fact]
    public async Task PublishAsync_propagates_bus_exceptions()
    {
        var bus = Substitute.For<IBus>();
        bus.Publish(Arg.Any<object>()).Returns(Task.FromException(new InvalidOperationException("bus error")));
        var publisher = new RebusDomainEventPublisher(bus);

        var act = () => publisher.PublishAsync(new TestDomainEvent("fail"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("bus error");
    }

    private sealed record TestDomainEvent(string Id) : IDomainEvent;
}

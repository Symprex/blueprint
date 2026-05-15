using Blueprint.Model.Messaging;
using FluentAssertions;
using Rebus.Messages;

namespace Blueprint.Rebus.Tests;

public class BusMessageAttributeRouterTests
{
    [Fact]
    public async Task GetDestinationAddress_returns_owner_queue_from_attribute()
    {
        var router = new BusMessageAttributeRouter();
        var message = new Message(new Dictionary<string, string>(), new TestEvent());

        var address = await router.GetDestinationAddress(message);

        address.Should().Be("billing-queue");
    }

    [Fact]
    public async Task GetDestinationAddress_returns_queue_even_when_topic_is_set()
    {
        var router = new BusMessageAttributeRouter();
        var message = new Message(new Dictionary<string, string>(), new TestEventWithTopic());

        var address = await router.GetDestinationAddress(message);

        address.Should().Be("identity-server");
    }

    [Fact]
    public async Task GetDestinationAddress_throws_when_attribute_missing()
    {
        var router = new BusMessageAttributeRouter();
        var message = new Message(new Dictionary<string, string>(), new UnattributedEvent());

        var act = () => router.GetDestinationAddress(message);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not have a [BusMessage] attribute*");
    }

    [Fact]
    public void GetDestinationAddress_throws_when_message_is_null()
    {
        var router = new BusMessageAttributeRouter();

        var act = () => router.GetDestinationAddress(null!);

        act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetOwnerAddress_resolves_type_from_topic_and_returns_queue()
    {
        var router = new BusMessageAttributeRouter();
        var topicName = typeof(TestEvent).AssemblyQualifiedName!;

        var address = await router.GetOwnerAddress(topicName);

        address.Should().Be("billing-queue");
    }

    [Fact]
    public async Task GetOwnerAddress_throws_for_unresolvable_topic()
    {
        var router = new BusMessageAttributeRouter();

        var act = () => router.GetOwnerAddress("NonExistent.Type, FakeAssembly");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*could not be resolved to a .NET type*");
    }

    [Fact]
    public async Task Caches_attribute_lookups_across_calls()
    {
        var router = new BusMessageAttributeRouter();
        var message1 = new Message(new Dictionary<string, string>(), new TestEvent());
        var message2 = new Message(new Dictionary<string, string>(), new TestEvent());

        var address1 = await router.GetDestinationAddress(message1);
        var address2 = await router.GetDestinationAddress(message2);

        address1.Should().Be("billing-queue");
        address2.Should().Be("billing-queue");
    }

    [BusMessage("billing-queue")]
    private sealed class TestEvent;

    [BusMessage("identity-server", topicName: "tenant-events")]
    private sealed class TestEventWithTopic;

    private sealed class UnattributedEvent;
}

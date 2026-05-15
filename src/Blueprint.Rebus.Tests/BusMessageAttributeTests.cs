using Blueprint.Model.Messaging;
using FluentAssertions;

namespace Blueprint.Rebus.Tests;

public class BusMessageAttributeTests
{
    [Fact]
    public void Constructor_sets_owner_queue_name()
    {
        var attr = new BusMessageAttribute("my-queue");

        attr.OwnerQueueName.Should().Be("my-queue");
        attr.TopicName.Should().BeNull();
    }

    [Fact]
    public void Constructor_sets_owner_queue_name_and_topic()
    {
        var attr = new BusMessageAttribute("my-queue", topicName: "my-topic");

        attr.OwnerQueueName.Should().Be("my-queue");
        attr.TopicName.Should().Be("my-topic");
    }

    [Fact]
    public void Constructor_throws_for_null_queue_name()
    {
        var act = () => new BusMessageAttribute(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_throws_for_empty_queue_name()
    {
        var act = () => new BusMessageAttribute(string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_throws_for_whitespace_queue_name()
    {
        var act = () => new BusMessageAttribute("   ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Attribute_is_not_inherited()
    {
        var attr = typeof(BusMessageAttribute).GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        attr.Inherited.Should().BeFalse();
    }

    [Fact]
    public void Attribute_targets_classes_only()
    {
        var attr = typeof(BusMessageAttribute).GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        attr.ValidOn.Should().Be(AttributeTargets.Class);
    }
}

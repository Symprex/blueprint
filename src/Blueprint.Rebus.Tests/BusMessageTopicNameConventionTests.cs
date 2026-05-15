using Blueprint.Model.Messaging;
using FluentAssertions;

namespace Blueprint.Rebus.Tests;

public class BusMessageTopicNameConventionTests
{
    [Fact]
    public void GetTopic_returns_explicit_topic_name_from_attribute()
    {
        var convention = new BusMessageTopicNameConvention();

        var topic = convention.GetTopic(typeof(EventWithTopic));

        topic.Should().Be("tenant-events");
    }

    [Fact]
    public void GetTopic_returns_simple_assembly_qualified_name_when_no_topic()
    {
        var convention = new BusMessageTopicNameConvention();

        var topic = convention.GetTopic(typeof(EventWithoutTopic));

        // Should be "{FullName}, {AssemblyName}" — Rebus default format
        topic.Should().Contain(typeof(EventWithoutTopic).FullName!);
        topic.Should().Contain(typeof(EventWithoutTopic).Assembly.GetName().Name!);
        topic.Should().NotContain("Version=");
    }

    [Fact]
    public void GetTopic_returns_simple_assembly_qualified_name_when_no_attribute()
    {
        var convention = new BusMessageTopicNameConvention();

        var topic = convention.GetTopic(typeof(PlainEvent));

        topic.Should().Contain(typeof(PlainEvent).FullName!);
        topic.Should().Contain(typeof(PlainEvent).Assembly.GetName().Name!);
    }

    [Fact]
    public void GetTopic_explicit_topic_takes_precedence_over_default()
    {
        var convention = new BusMessageTopicNameConvention();

        var topicWithExplicit = convention.GetTopic(typeof(EventWithTopic));
        var topicDefault = convention.GetTopic(typeof(EventWithoutTopic));

        // Explicit topic should be the short custom name
        topicWithExplicit.Should().Be("tenant-events");

        // Default should be the assembly-qualified name
        topicDefault.Should().NotBe("tenant-events");
    }

    [Fact]
    public void Subclass_can_override_default_topic_naming()
    {
        var convention = new KebabCaseTopicConvention();

        var topic = convention.GetTopic(typeof(PlainEvent));

        topic.Should().Be("plain-event");
    }

    [Fact]
    public void Subclass_override_does_not_affect_explicit_topics()
    {
        var convention = new KebabCaseTopicConvention();

        var topic = convention.GetTopic(typeof(EventWithTopic));

        // Explicit topic from attribute should still win
        topic.Should().Be("tenant-events");
    }

    [BusMessage("identity-server", topicName: "tenant-events")]
    private sealed class EventWithTopic;

    [BusMessage("billing-queue")]
    private sealed class EventWithoutTopic;

    private sealed class PlainEvent;

    private sealed class KebabCaseTopicConvention : BusMessageTopicNameConvention
    {
        protected override string GetDefaultTopicName(Type eventType)
        {
            return ToKebabCase(eventType.Name);
        }

        private static string ToKebabCase(string name)
        {
            var chars = new List<char>();

            for (var i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]))
                {
                    chars.Add('-');
                }

                chars.Add(char.ToLowerInvariant(name[i]));
            }

            return new string(chars.ToArray());
        }
    }
}

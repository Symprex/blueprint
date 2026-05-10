using FluentAssertions;

namespace Blueprint.Rebus.Tests;

/// <summary>
/// Proves that <see cref="BlueprintRebusConfiguration"/> correctly tracks subscribed message types.
/// </summary>
public class BlueprintRebusConfigurationTests
{
    [Fact]
    public void Subscribe_adds_type_to_subscribed_messages()
    {
        var config = new BlueprintRebusConfiguration();

        config.Subscribe<TestMessage1>();
        config.Subscribe<TestMessage2>();

        config.SubscribedMessages.Should().HaveCount(2);
        config.SubscribedMessages.Should().Contain(typeof(TestMessage1));
        config.SubscribedMessages.Should().Contain(typeof(TestMessage2));
    }

    [Fact]
    public void Subscribe_returns_self_for_chaining()
    {
        var config = new BlueprintRebusConfiguration();

        var result = config.Subscribe<TestMessage1>();

        result.Should().BeSameAs(config);
    }

    [Fact]
    public void SubscribedMessages_starts_empty()
    {
        var config = new BlueprintRebusConfiguration();

        config.SubscribedMessages.Should().BeEmpty();
    }

    private class TestMessage1;

    private class TestMessage2;
}

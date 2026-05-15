using Blueprint.Model.Messaging;
using Rebus.Config;
using Rebus.Routing;
using Rebus.Topic;

namespace Blueprint.Rebus;

/// <summary>
/// Extension methods for configuring <see cref="BusMessageAttribute"/>-based routing and topic
/// naming on Rebus configurers.
/// </summary>
public static class BusMessageRoutingExtensions
{
    /// <summary>
    /// Configures Rebus to use <see cref="BusMessageAttributeRouter"/> for message routing and
    /// <see cref="BusMessageTopicNameConvention"/> for topic naming. Both read from
    /// <see cref="BusMessageAttribute"/> on message types.
    /// </summary>
    /// <param name="configurer">The Rebus routing configurer.</param>
    /// <example>
    /// <code>
    /// services.AddBlueprintRebus((sp, rebusConfigurer) => rebusConfigurer
    ///     .Routing(r => r.UseBusMessageAttributeRouting())
    ///     .Transport(t => t.UseAzureServiceBus(connectionString, "my-queue")));
    /// </code>
    /// </example>
    public static void UseBusMessageAttributeRouting(this StandardConfigurer<IRouter> configurer)
    {
        ArgumentNullException.ThrowIfNull(configurer);

        var resolver = new BusMessageRoutingResolver();

        configurer.Register(_ => new BusMessageAttributeRouter(resolver));
        configurer.OtherService<ITopicNameConvention>().Register(_ => new BusMessageTopicNameConvention(resolver));
    }

    /// <summary>
    /// Configures Rebus to use <see cref="BusMessageAttributeRouter"/> for message routing and
    /// a custom <see cref="ITopicNameConvention"/> for topic naming.
    /// </summary>
    /// <param name="configurer">The Rebus routing configurer.</param>
    /// <param name="topicConvention">
    /// A custom topic name convention. Use this when your application has specific topic naming
    /// requirements (e.g. namespace stripping, kebab-casing). The convention should still respect
    /// <see cref="BusMessageAttribute.TopicName"/> when present.
    /// </param>
    public static void UseBusMessageAttributeRouting(
        this StandardConfigurer<IRouter> configurer,
        ITopicNameConvention topicConvention)
    {
        ArgumentNullException.ThrowIfNull(configurer);
        ArgumentNullException.ThrowIfNull(topicConvention);

        configurer.Register(_ => new BusMessageAttributeRouter());
        configurer.OtherService<ITopicNameConvention>().Register(_ => topicConvention);
    }
}

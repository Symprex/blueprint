using Blueprint.Model.Messaging;
using Rebus.Messages;
using Rebus.Routing;

namespace Blueprint.Rebus;

/// <summary>
/// A Rebus <see cref="IRouter"/> that reads <see cref="BusMessageAttribute"/> from message types
/// to determine the destination queue for <c>Send</c> operations and the owner queue for
/// topic-based <c>Publish</c>/<c>Subscribe</c> operations.
/// </summary>
/// <remarks>
/// <para>
/// This router resolves the destination address from <see cref="BusMessageAttribute.OwnerQueueName"/>
/// and caches the result per message type. It works in tandem with <see cref="BusMessageTopicNameConvention"/>
/// which reads <see cref="BusMessageAttribute.TopicName"/> from the same attribute.
/// </para>
/// <para>
/// For topic ownership resolution (<see cref="GetOwnerAddress"/>), the router reconstructs the
/// message type from the topic string (which is typically the assembly-qualified type name) and
/// then reads the owner queue from the attribute. When using custom topic names via
/// <see cref="BusMessageAttribute.TopicName"/>, ensure that a matching
/// <see cref="BusMessageTopicNameConvention"/> (or subclass) is also registered.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// services.AddBlueprintRebus((sp, configurer) => configurer
///     .Routing(r => r.UseBusMessageAttributeRouting())
///     .Transport(t => t.UseAzureServiceBus(connectionString, "my-queue")));
/// </code>
/// </example>
public class BusMessageAttributeRouter : IRouter
{
    private readonly BusMessageRoutingResolver _resolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="BusMessageAttributeRouter"/> class.
    /// </summary>
    public BusMessageAttributeRouter()
    {
        this._resolver = new BusMessageRoutingResolver();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BusMessageAttributeRouter"/> class using
    /// a shared <see cref="BusMessageRoutingResolver"/> instance.
    /// </summary>
    /// <param name="resolver">The shared resolver (also used by <see cref="BusMessageTopicNameConvention"/>).</param>
    internal BusMessageAttributeRouter(BusMessageRoutingResolver resolver)
    {
        this._resolver = resolver;
    }

    /// <inheritdoc />
    public Task<string> GetDestinationAddress(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Body is null)
        {
            throw new ArgumentException(
                "Message body cannot be null when using attribute-based routing.",
                nameof(message));
        }

        var attribute = this._resolver.ResolveRequired(message.Body.GetType());

        return Task.FromResult(attribute.OwnerQueueName);
    }

    /// <inheritdoc />
    public Task<string> GetOwnerAddress(string topic)
    {
        ArgumentNullException.ThrowIfNull(topic);

        var messageType = ResolveTypeFromTopic(topic);
        var attribute = this._resolver.ResolveRequired(messageType);

        return Task.FromResult(attribute.OwnerQueueName);
    }

    private static Type ResolveTypeFromTopic(string topic)
    {
        try
        {
            return Type.GetType(topic, throwOnError: true, ignoreCase: true)!;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"The topic '{topic}' could not be resolved to a .NET type. When using " +
                $"attribute-based routing, topics must be resolvable type names. If you are " +
                $"using custom topic names via [BusMessage(topicName: \"...\")], ensure a " +
                $"matching ITopicNameConvention is registered.",
                ex);
        }
    }
}

using Blueprint.Model.Messaging;
using Rebus.Topic;

namespace Blueprint.Rebus;

/// <summary>
/// A Rebus <see cref="ITopicNameConvention"/> that reads <see cref="BusMessageAttribute.TopicName"/>
/// from message types. When the attribute specifies a topic name, that value is used directly.
/// Otherwise, falls back to the default Rebus convention (simple assembly-qualified type name).
/// </summary>
/// <remarks>
/// <para>
/// This convention works in tandem with <see cref="BusMessageAttributeRouter"/>: the router handles
/// queue ownership while this convention handles topic naming for publish/subscribe scenarios.
/// Both read from the same <see cref="BusMessageAttribute"/>.
/// </para>
/// <para>
/// Consumers with custom topic naming requirements (e.g. namespace stripping, kebab-casing)
/// should subclass this and override <see cref="GetDefaultTopicName"/> to provide their
/// application-specific fallback behavior.
/// </para>
/// </remarks>
public class BusMessageTopicNameConvention : ITopicNameConvention
{
    private readonly BusMessageRoutingResolver _resolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="BusMessageTopicNameConvention"/> class.
    /// </summary>
    public BusMessageTopicNameConvention()
    {
        this._resolver = new BusMessageRoutingResolver();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BusMessageTopicNameConvention"/> class using
    /// a shared <see cref="BusMessageRoutingResolver"/> instance.
    /// </summary>
    /// <param name="resolver">The shared resolver (also used by <see cref="BusMessageAttributeRouter"/>).</param>
    internal BusMessageTopicNameConvention(BusMessageRoutingResolver resolver)
    {
        this._resolver = resolver;
    }

    /// <inheritdoc />
    public string GetTopic(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        var attribute = this._resolver.Resolve(eventType);

        if (attribute?.TopicName is not null)
        {
            return attribute.TopicName;
        }

        return this.GetDefaultTopicName(eventType);
    }

    /// <summary>
    /// Gets the default topic name when no <see cref="BusMessageAttribute.TopicName"/> is specified.
    /// The base implementation uses Rebus' default convention: the simple assembly-qualified type name
    /// (e.g. <c>MyNamespace.MyEvent, MyAssembly</c>).
    /// </summary>
    /// <param name="eventType">The event type to get the topic name for.</param>
    /// <returns>The topic name string.</returns>
    /// <remarks>
    /// Override this method to provide application-specific fallback topic naming (e.g. namespace
    /// stripping, kebab-casing, or other conventions).
    /// </remarks>
    protected virtual string GetDefaultTopicName(Type eventType)
    {
        // Rebus' DefaultTopicNameConvention uses GetSimpleAssemblyQualifiedName which is
        // "{FullName}, {AssemblyName}" without version/culture/public-key details.
        var assemblyName = eventType.Assembly.GetName().Name;

        return eventType.FullName is not null
            ? $"{eventType.FullName}, {assemblyName}"
            : eventType.Name;
    }
}

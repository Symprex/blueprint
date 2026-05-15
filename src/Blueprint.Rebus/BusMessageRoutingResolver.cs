using System.Collections.Concurrent;
using System.Reflection;
using Blueprint.Model.Messaging;

namespace Blueprint.Rebus;

/// <summary>
/// Shared routing metadata resolver that reads <see cref="BusMessageAttribute"/> from message
/// types and caches the results. Both <see cref="BusMessageAttributeRouter"/> and
/// <see cref="BusMessageTopicNameConvention"/> use this to ensure consistent routing decisions.
/// </summary>
internal sealed class BusMessageRoutingResolver
{
    private readonly ConcurrentDictionary<Type, BusMessageAttribute?> _cache = new();

    /// <summary>
    /// Resolves the <see cref="BusMessageAttribute"/> for the given message type, caching the
    /// result for subsequent lookups.
    /// </summary>
    /// <param name="messageType">The message type to resolve routing metadata for.</param>
    /// <returns>The attribute instance, or <c>null</c> if the type has no <see cref="BusMessageAttribute"/>.</returns>
    public BusMessageAttribute? Resolve(Type messageType)
    {
        return this._cache.GetOrAdd(
            messageType,
            static t => t.GetCustomAttribute<BusMessageAttribute>());
    }

    /// <summary>
    /// Resolves the <see cref="BusMessageAttribute"/> for the given message type, throwing an
    /// <see cref="InvalidOperationException"/> if the attribute is missing.
    /// </summary>
    /// <param name="messageType">The message type to resolve routing metadata for.</param>
    /// <returns>The attribute instance.</returns>
    /// <exception cref="InvalidOperationException">The message type does not have a <see cref="BusMessageAttribute"/>.</exception>
    public BusMessageAttribute ResolveRequired(Type messageType)
    {
        return this.Resolve(messageType)
            ?? throw new InvalidOperationException(
                $"Cannot determine routing for message type {messageType.FullName} because it does not have a " +
                $"[BusMessage] attribute. Ensure all message types that are sent, published, or subscribed " +
                $"have a [BusMessage(\"ownerQueueName\")] attribute applied.");
    }
}

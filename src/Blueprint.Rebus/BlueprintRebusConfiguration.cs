namespace Blueprint.Rebus;

/// <summary>
/// Configuration for the Blueprint Rebus integration, tracking which message types
/// have been subscribed to and should be executed through the Blueprint pipeline.
/// </summary>
public sealed class BlueprintRebusConfiguration
{
    private readonly List<Type> _subscribedMessages = new();

    /// <summary>
    /// Gets all message types that have been subscribed to. These types will be registered
    /// as Blueprint operations and <c>IBus.Subscribe</c> will be called on startup.
    /// </summary>
    public IReadOnlyList<Type> SubscribedMessages => this._subscribedMessages;

    /// <summary>
    /// Subscribes to the given message type, meaning that message type will be registered
    /// with Blueprint as an operation and <c>IBus.Subscribe{TEvent}</c> will be called on
    /// startup to listen for this message.
    /// </summary>
    /// <typeparam name="T">The type of message to subscribe to.</typeparam>
    /// <returns>This configuration instance for chaining.</returns>
    public BlueprintRebusConfiguration Subscribe<T>()
    {
        this._subscribedMessages.Add(typeof(T));
        return this;
    }
}

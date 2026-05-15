namespace Blueprint.Model.Messaging;

/// <summary>
/// Declares the message routing metadata for a Rebus message type, specifying which queue
/// owns the message and optionally which topic it is published to.
/// </summary>
/// <remarks>
/// <para>
/// Rebus messages can be delivered via <c>Send</c> (to one queue) or <c>Publish</c> (to
/// all subscribers of a topic). This attribute encodes both patterns:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       <see cref="OwnerQueueName"/> is the destination address for <c>Send</c> and the
///       ownership endpoint that Rebus uses to resolve topic subscriptions.
///     </description>
///   </item>
///   <item>
///     <description>
///       <see cref="TopicName"/> (optional) overrides the default topic name for
///       <c>Publish</c>/<c>Subscribe</c> scenarios. When <c>null</c>, Rebus' default
///       topic naming convention applies.
///     </description>
///   </item>
/// </list>
/// <para>
/// Place this attribute on shared message/event contract types so that routing is
/// co-located with the contract rather than scattered across service startup code.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Queue routing only — messages are sent directly to the "billing" queue
/// [BusMessage("billing")]
/// public class ProcessPaymentCommand { }
///
/// // Topic-based routing — messages are published to the "tenant-events" topic,
/// // owned by the "identity-server" queue
/// [BusMessage("identity-server", topicName: "tenant-events")]
/// public class TenantSignedUpEvent { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BusMessageAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BusMessageAttribute"/> class.
    /// </summary>
    /// <param name="ownerQueueName">The owner queue address for this message.</param>
    /// <param name="topicName">
    /// The topic name for publish/subscribe scenarios. When <c>null</c>, Rebus'
    /// default topic naming convention (typically the simple assembly-qualified type name) is used.
    /// </param>
    public BusMessageAttribute(string ownerQueueName, string? topicName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerQueueName);

        this.OwnerQueueName = ownerQueueName;
        this.TopicName = topicName;
    }

    /// <summary>
    /// The owner queue address for this message. Used by <c>Send</c> and as the
    /// ownership endpoint for topic-based routing.
    /// </summary>
    public string OwnerQueueName { get; }

    /// <summary>
    /// The topic name for publish/subscribe scenarios, or <c>null</c> to use
    /// the default Rebus topic naming convention.
    /// </summary>
    public string? TopicName { get; }
}

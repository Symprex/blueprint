namespace Blueprint.Model.Attributes;

/// <summary>
/// Declares the stable resource-event identifier that later descriptor and OpenAPI surfaces can project from a
/// body-bearing resource-event payload type.
/// </summary>
/// <param name="eventId">The stable resource-event identifier that should appear as the serialized `eventId` discriminator.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ResourceEventAttribute(string eventId) : Attribute
{
    /// <summary>
    /// Gets the stable resource-event identifier that later descriptor and OpenAPI surfaces should publish.
    /// </summary>
    public string EventId { get; } = eventId;
}

namespace Blueprint.Model.Attributes;

/// <summary>
/// Declares a nested partial record within a resource-event family as a concrete event outcome, carrying the coarse
/// change type and a stable reason string that together form the event's identity for wire contracts and auditing.
/// </summary>
/// <param name="changeType">The coarse kind of resource mutation this event represents.</param>
/// <param name="reason">
/// The stable reason string that distinguishes this event within its family. Combined with the family's
/// <see cref="ResourceEventFamilyAttribute.ResourceObject"/> to produce the generated event ID
/// (e.g., <c>"template.created"</c>).
/// </param>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ResourceEventReasonAttribute(ChangeType changeType, string reason) : Attribute
{
    /// <summary>
    /// Gets the coarse kind of resource mutation this event represents.
    /// </summary>
    public ChangeType ChangeType { get; } = changeType;

    /// <summary>
    /// Gets the stable reason string that distinguishes this event within its family.
    /// </summary>
    public string Reason { get; } = reason;
}

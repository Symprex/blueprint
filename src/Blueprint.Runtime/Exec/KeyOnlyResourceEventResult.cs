namespace Blueprint.Runtime.Exec;

/// <summary>
/// Represents a host-neutral resource-event success outcome that intentionally returns no response body while still
/// carrying stable event and resource identity for later host translation, auditing, and inspection surfaces.
/// </summary>
public sealed class KeyOnlyResourceEventResult : OperationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KeyOnlyResourceEventResult"/> class.
    /// </summary>
    /// <param name="eventId">The stable event identity for the semantic resource-event outcome.</param>
    /// <param name="resourceId">The stable resource identity associated with the event.</param>
    public KeyOnlyResourceEventResult(string eventId, string resourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        this.EventId = eventId;
        this.ResourceId = resourceId;
    }

    /// <summary>
    /// Gets the stable event identity that downstream hosts or tooling can expose without requiring a response body.
    /// </summary>
    public string EventId { get; }

    /// <summary>
    /// Gets the stable resource identity associated with the event.
    /// </summary>
    public string ResourceId { get; }

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        throw new NotSupportedException("KeyOnlyResourceEventResult must be translated by a host-specific result executor instead of self-executing.");
    }
}

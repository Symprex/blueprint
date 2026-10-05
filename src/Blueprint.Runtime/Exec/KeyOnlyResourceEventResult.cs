using Blueprint.Model;

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
    /// <param name="changeType">The coarse mutation kind (created, updated, deleted) that determines host-level status codes.</param>
    /// <param name="eventId">The stable event identity for the semantic resource-event outcome.</param>
    /// <param name="resourceId">The stable resource identity associated with the event.</param>
    public KeyOnlyResourceEventResult(ChangeType changeType, string eventId, string resourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        this.ChangeType = changeType;
        this.EventId = eventId;
        this.ResourceId = resourceId;
    }

    /// <summary>
    /// Initializes the result <c>BlueprintHttpClient</c> reads back from the empty response the HTTP host wrote for one.
    /// Only the change type crosses the wire, as the status the host chose from it, so both identities are empty.
    /// </summary>
    /// <param name="changeType">The mutation kind the response's status stands for.</param>
    internal KeyOnlyResourceEventResult(ChangeType changeType)
    {
        this.ChangeType = changeType;
        this.EventId = string.Empty;
        this.ResourceId = string.Empty;
    }

    /// <summary>
    /// Gets the coarse mutation kind that the HTTP host uses to select the appropriate status code
    /// (201 Created, 200 OK, or 204 No Content).
    /// </summary>
    public ChangeType ChangeType { get; }

    /// <summary>
    /// Gets the stable event identity that downstream hosts or tooling can expose without requiring a response body.
    /// Empty on a result read back from an HTTP response, which carries no identity.
    /// </summary>
    public string EventId { get; }

    /// <summary>
    /// Gets the stable resource identity associated with the event. Empty on a result read back from an HTTP response,
    /// which carries no identity.
    /// </summary>
    public string ResourceId { get; }

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        throw new NotSupportedException("KeyOnlyResourceEventResult must be translated by a host-specific result executor instead of self-executing.");
    }
}

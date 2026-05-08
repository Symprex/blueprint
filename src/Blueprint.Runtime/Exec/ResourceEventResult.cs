using Blueprint.Model;

namespace Blueprint.Runtime.Exec;

/// <summary>
/// Represents a host-neutral resource-event success outcome that carries stable event/resource identity plus a response
/// payload, keeping body-bearing resource-event flows distinct from ordinary created-resource responses.
/// </summary>
public class ResourceEventResult : OperationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceEventResult"/> class.
    /// </summary>
    /// <param name="changeType">The coarse mutation kind (created, updated, deleted) that determines host-level status codes.</param>
    /// <param name="eventId">The stable event identity for the semantic resource-event outcome.</param>
    /// <param name="resourceId">The stable resource identity associated with the event.</param>
    /// <param name="content">The resource-event payload that hosts may serialize in the response body.</param>
    public ResourceEventResult(ChangeType changeType, string eventId, string resourceId, object content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentNullException.ThrowIfNull(content);

        this.ChangeType = changeType;
        this.EventId = eventId;
        this.ResourceId = resourceId;
        this.Content = content;
    }

    /// <summary>
    /// Gets the coarse mutation kind that the HTTP host uses to select the appropriate status code
    /// (201 Created, 200 OK, or 204 No Content).
    /// </summary>
    public ChangeType ChangeType { get; }

    /// <summary>
    /// Gets the stable event identity that later hosts and tooling can use as a body discriminator.
    /// </summary>
    public string EventId { get; }

    /// <summary>
    /// Gets the stable resource identity associated with the event.
    /// </summary>
    public string ResourceId { get; }

    /// <summary>
    /// Gets the resource-event payload that host executors can serialize when materializing the semantic result.
    /// </summary>
    public object Content { get; }

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        throw new NotSupportedException("ResourceEventResult must be translated by a host-specific result executor instead of self-executing.");
    }

    /// <inheritdoc />
    public override T Unwrap<T>()
    {
        return this.Content is T result
            ? result
            : throw new InvalidCastException("Cannot cast resource-event result content of type " + this.Content.GetType().Name + " to " + typeof(T).Name);
    }
}

/// <summary>
/// Represents a host-neutral resource-event success outcome with a typed payload.
/// </summary>
/// <typeparam name="T">The type of payload that will be serialized by the host.</typeparam>
public sealed class ResourceEventResult<T> : ResourceEventResult where T : notnull
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceEventResult{T}"/> class.
    /// </summary>
    /// <param name="changeType">The coarse mutation kind (created, updated, deleted) that determines host-level status codes.</param>
    /// <param name="eventId">The stable event identity for the semantic resource-event outcome.</param>
    /// <param name="resourceId">The stable resource identity associated with the event.</param>
    /// <param name="content">The resource-event payload.</param>
    public ResourceEventResult(ChangeType changeType, string eventId, string resourceId, T content)
        : base(changeType, eventId, resourceId, content)
    {
    }

    /// <summary>
    /// Gets the resource-event payload with its original static type.
    /// </summary>
    public new T Content => (T)base.Content;
}

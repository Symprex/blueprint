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

    /// <summary>
    /// Gets a metadata dictionary of key-value pairs associated with this event that can be
    /// persisted alongside the event for auditing or enrichment purposes.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; private set; }

    /// <summary>
    /// Gets a dictionary of security-related data that will NOT be persisted anywhere.
    /// Use this for sensitive values that should be available in the HTTP response
    /// (e.g. temporary tokens) but must not reach audit or event stores.
    /// </summary>
    public Dictionary<string, object>? SecureData { get; private set; }

    /// <summary>
    /// Gets a dictionary of property names to their previous values, used to identify what
    /// has actually changed between the old and new state of the resource.
    /// </summary>
    public Dictionary<string, object>? ChangedValues { get; private set; }

    /// <summary>
    /// Gets or sets the correlation identifier that ties this event back to the initial
    /// request that produced it, useful for cross-service tracing and event replay.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Adds a metadata key-value pair to this event, returning this instance for fluent chaining.
    /// Metadata is persisted with the event for auditing and downstream enrichment.
    /// </summary>
    /// <param name="key">The metadata key.</param>
    /// <param name="value">The metadata value.</param>
    /// <returns>This <see cref="ResourceEventResult"/> for chaining.</returns>
    public ResourceEventResult WithMetadata(string key, object value)
    {
        this.Metadata ??= new Dictionary<string, object>();
        this.Metadata[key] = value;
        return this;
    }

    /// <summary>
    /// Adds a security-related key-value pair to this event. Secure data appears in the HTTP
    /// response but is NOT persisted to audit or event stores. Returns this instance for chaining.
    /// </summary>
    /// <param name="key">The secure data key.</param>
    /// <param name="value">The secure data value.</param>
    /// <returns>This <see cref="ResourceEventResult"/> for chaining.</returns>
    public ResourceEventResult WithSecureData(string key, object value)
    {
        this.SecureData ??= new Dictionary<string, object>();
        this.SecureData[key] = value;
        return this;
    }

    /// <summary>
    /// Adds a changed-value entry to this event, recording the previous value of a property.
    /// Returns this instance for chaining.
    /// </summary>
    /// <param name="propertyName">The name of the changed property.</param>
    /// <param name="previousValue">The previous value of the property.</param>
    /// <returns>This <see cref="ResourceEventResult"/> for chaining.</returns>
    public ResourceEventResult WithChangedValue(string propertyName, object previousValue)
    {
        this.ChangedValues ??= new Dictionary<string, object>();
        this.ChangedValues[propertyName] = previousValue;
        return this;
    }

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
public class ResourceEventResult<T> : ResourceEventResult where T : notnull
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

using System.Diagnostics.CodeAnalysis;
using Blueprint.Model;

namespace Blueprint.Runtime.Exec;

/// <summary>
/// Represents a resource-event result whose content is deferred until a self-query is executed through the pipeline.
/// The handler provides the query operation and optionally a resource key; the <c>ResourceEventSelfQueryModule</c>
/// resolves the query at pipeline execution time and populates <see cref="ResolvedContent"/> so that downstream
/// modules (link generation, audit, HTTP executor) can access the resolved resource.
/// </summary>
/// <remarks>
/// <para>
/// This type exists to support the migration path from legacy <c>ResourceEvent</c> patterns where handlers
/// return a domain object and the infrastructure resolves the full API resource via a nested query execution.
/// New code should prefer returning <see cref="ResourceEventResult{T}"/> directly where feasible.
/// </para>
/// <para>
/// The instance is mutated in-place (like legacy <c>ResourceEvent.Data</c> population) rather than
/// replaced, preserving the pipeline's <c>handlerResult</c> variable type without requiring reassignment.
/// </para>
/// </remarks>
/// <typeparam name="T">The expected API resource type that the self-query will resolve to.</typeparam>
public sealed class DeferredResourceEventResult<T> : OperationResult, IDeferredResourceEventResult, IResourceEventResourceType where T : notnull
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DeferredResourceEventResult{T}"/> class.
    /// </summary>
    /// <param name="changeType">The coarse mutation kind (created, updated, deleted) that determines host-level status codes.</param>
    /// <param name="eventId">The stable event identity for the semantic resource-event outcome.</param>
    /// <param name="selfQuery">The query operation to execute through the pipeline to resolve the event's resource content.</param>
    public DeferredResourceEventResult(ChangeType changeType, string eventId, object selfQuery)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentNullException.ThrowIfNull(selfQuery);

        this.ChangeType = changeType;
        this.EventId = eventId;
        this.SelfQuery = selfQuery;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DeferredResourceEventResult{T}"/> class with an explicit resource key.
    /// </summary>
    /// <param name="changeType">The coarse mutation kind (created, updated, deleted) that determines host-level status codes.</param>
    /// <param name="eventId">The stable event identity for the semantic resource-event outcome.</param>
    /// <param name="resourceId">The stable resource identity known upfront, before query resolution.</param>
    /// <param name="selfQuery">The query operation to execute through the pipeline to resolve the event's resource content.</param>
    public DeferredResourceEventResult(ChangeType changeType, string eventId, string resourceId, object selfQuery)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentNullException.ThrowIfNull(selfQuery);

        this.ChangeType = changeType;
        this.EventId = eventId;
        this.ResourceId = resourceId;
        this.SelfQuery = selfQuery;
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
    /// Gets the stable resource identity. When not specified in the constructor, this is derived
    /// from the resolved content's <c>Id</c> property after <see cref="SetResolvedContent"/> is called.
    /// </summary>
    public string? ResourceId { get; private set; }

    /// <summary>
    /// Gets the query operation that the pipeline module will execute to resolve the event's resource content.
    /// </summary>
    public object SelfQuery { get; }

    /// <summary>
    /// Gets the resolved resource content after the self-query has been executed.
    /// Null until <see cref="SetResolvedContent"/> is called by the pipeline module.
    /// </summary>
    public object? ResolvedContent { get; private set; }

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
    /// Gets or sets the correlation identifier that ties this event back to the initial
    /// request that produced it, useful for cross-service tracing and event replay.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <inheritdoc />
    Type IResourceEventResourceType.ResourceType => typeof(T);

    /// <summary>
    /// Adds a metadata key-value pair to this event, returning this instance for fluent chaining.
    /// </summary>
    /// <param name="key">The metadata key.</param>
    /// <param name="value">The metadata value.</param>
    /// <returns>This instance for chaining.</returns>
    public DeferredResourceEventResult<T> WithMetadata(string key, object value)
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
    /// <returns>This instance for chaining.</returns>
    public DeferredResourceEventResult<T> WithSecureData(string key, object value)
    {
        this.SecureData ??= new Dictionary<string, object>();
        this.SecureData[key] = value;
        return this;
    }

    /// <inheritdoc />
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Resource types are concrete API model classes preserved by the application.")]
    public void SetResolvedContent(object content)
    {
        ArgumentNullException.ThrowIfNull(content);

        this.ResolvedContent = content;

        // Derive resource ID from resolved content if not set upfront
        this.ResourceId ??= DeriveResourceId(content);
    }

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        throw new NotSupportedException(
            "DeferredResourceEventResult must be resolved by ResourceEventSelfQueryModule before being processed by a host-specific result executor.");
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Resource types are concrete API model classes preserved by the application.")]
    private static string DeriveResourceId(object content)
    {
        var idProperty = content.GetType().GetProperty("Id");
        if (idProperty is not null)
        {
            var idValue = idProperty.GetValue(content);
            if (idValue is not null)
            {
                return idValue.ToString()!;
            }
        }

        return string.Empty;
    }
}

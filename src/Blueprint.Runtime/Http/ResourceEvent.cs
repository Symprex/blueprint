using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.Http;

/// <summary>
/// Represents an event that has happened within an API resulting in the modification, creation, or
/// deletion of an API resource. Handlers return instances of <see cref="ResourceEvent{TResource}"/>
/// (or the convenience subclasses <see cref="ResourceCreated{TResource}"/>,
/// <see cref="ResourceUpdated{TResource}"/>, <see cref="ResourceDeleted{TResource}"/>) as their
/// operation result and the framework translates them into appropriate HTTP responses.
/// </summary>
public class ResourceEvent : OperationResult
{
    private static readonly ConcurrentDictionary<Type, string> _typeNameCache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceEvent"/> class with an inline resource body.
    /// </summary>
    /// <param name="changeType">The type of change.</param>
    /// <param name="eventId">The id of this specific event.</param>
    /// <param name="data">The API resource this event applies to.</param>
    [JsonConstructor]
    public ResourceEvent(ResourceEventChangeType changeType, string eventId, object data)
    {
        ArgumentNullException.ThrowIfNull(data);

        this.ChangeType = changeType;
        this.EventId = eventId;
        this.ResourceType = data.GetType();
        this.ResourceObject = GetResourceTypeName(this.ResourceType);
        this.Data = data;
        this.Created = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceEvent"/> class with a self-query
    /// that can be executed to load the associated resource.
    /// </summary>
    /// <param name="changeType">The type of change.</param>
    /// <param name="eventId">The id of this specific event.</param>
    /// <param name="resourceType">The type of resource that has been modified.</param>
    /// <param name="selfQuery">An operation that, when executed, will load the associated resource.</param>
    public ResourceEvent(ResourceEventChangeType changeType, string eventId, Type resourceType, IQuery selfQuery)
    {
        ArgumentNullException.ThrowIfNull(resourceType);
        ArgumentNullException.ThrowIfNull(selfQuery);

        this.ChangeType = changeType;
        this.EventId = eventId;
        this.ResourceType = resourceType;
        this.ResourceObject = GetResourceTypeName(resourceType);
        this.SelfQuery = selfQuery;
        this.Created = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the id of this event, for example <c>timeEntry.updated</c> or <c>account.approvals.enabled</c>.
    /// The id is a dot-delimited string with the first part being the resource type and the remainder
    /// identifying the specific event.
    /// </summary>
    [JsonInclude]
    public string EventId { get; internal set; }

    /// <summary>
    /// Gets the change type of this event, indicating whether the resource was created, updated, or deleted.
    /// </summary>
    [JsonInclude]
    public ResourceEventChangeType ChangeType { get; internal set; }

    /// <summary>
    /// Gets the created date of this event.
    /// </summary>
    [JsonInclude]
    public DateTimeOffset Created { get; internal set; }

    /// <summary>
    /// Gets the query that represents the operation that will load the resource this event represents.
    /// </summary>
    [JsonIgnore]
    public IQuery? SelfQuery { get; }

    /// <summary>
    /// Gets the type-name token of the resource (for example <c>"user"</c>, <c>"template"</c>).
    /// </summary>
    [JsonInclude]
    public string ResourceObject { get; internal set; }

    /// <summary>
    /// Gets or sets the href of the resource this event represents, populated by the framework
    /// from the resource type and id definition.
    /// </summary>
    [JsonInclude]
    public string? Href { get; set; }

    /// <summary>
    /// Gets or sets the payload of this event — the actual resource that this represents.
    /// </summary>
    [JsonInclude]
    public object? Data { get; set; }

    /// <summary>
    /// Gets or sets a dictionary of previous values, used to identify what has changed
    /// between two versions of a resource, keyed on the property name.
    /// </summary>
    [JsonInclude]
    public Dictionary<string, object>? ChangedValues { get; set; }

    /// <summary>
    /// Gets the metadata dictionary, a free-form bag of key-value pairs that can be stored
    /// against an event and may be persisted (for example for auditing).
    /// </summary>
    [JsonInclude]
    public Dictionary<string, object> Metadata { get; internal set; } = new();

    /// <summary>
    /// Gets a dictionary of security-related data that will NOT be persisted anywhere.
    /// </summary>
    [JsonInclude]
    public Dictionary<string, object> SecureData { get; internal set; } = new();

    /// <summary>
    /// Gets or sets the correlation id for this event, tying it back to the initial request.
    /// </summary>
    [JsonInclude]
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Gets or sets the operation that triggered the generation of this resource event.
    /// </summary>
    [JsonIgnore]
    public object? Operation { get; set; }

    /// <summary>
    /// The CLR type of the resource this event represents.
    /// </summary>
    [JsonIgnore]
    internal Type ResourceType { get; set; }

    /// <summary>
    /// Derives a conventional resource-type name from a <see cref="Type"/>, stripping common
    /// suffixes (<c>ApiResource</c>, <c>Resource</c>, <c>DTO</c>) and camelCasing the result.
    /// </summary>
    /// <param name="resourceType">The CLR type to derive the resource name from.</param>
    /// <returns>The camelCased resource type name (e.g. <c>"tenant"</c> from <c>TenantApiResource</c>).</returns>
    public static string GetResourceTypeName(Type resourceType)
    {
        return _typeNameCache.GetOrAdd(resourceType, static t =>
        {
            var name = t.Name;

            if (name.EndsWith("ApiResource", StringComparison.Ordinal))
            {
                name = name[..^"ApiResource".Length];
            }
            else if (name.EndsWith("Resource", StringComparison.Ordinal))
            {
                name = name[..^"Resource".Length];
            }
            else if (name.EndsWith("DTO", StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^3];
            }

            // CamelCase: lowercase the first character
            if (name.Length > 0 && char.IsUpper(name[0]))
            {
                return string.Concat(char.ToLowerInvariant(name[0]).ToString(), name.AsSpan(1));
            }

            return name;
        });
    }

    /// <summary>
    /// Adds the given key-value pair to this event's <see cref="Metadata"/> dictionary.
    /// </summary>
    /// <param name="key">The key of the item to add.</param>
    /// <param name="value">The value of the item to add.</param>
    /// <returns>This <see cref="ResourceEvent"/> to support further chaining.</returns>
    public ResourceEvent WithMetadata(string key, object value)
    {
        this.Metadata[key] = value;
        return this;
    }

    /// <summary>
    /// Adds the given key-value pair to this event's <see cref="SecureData"/> dictionary,
    /// which must not be persisted.
    /// </summary>
    /// <param name="key">The key of the item to add.</param>
    /// <param name="value">The value of the item to add.</param>
    /// <returns>This <see cref="ResourceEvent"/> to support further chaining.</returns>
    public ResourceEvent WithSecureData(string key, object value)
    {
        this.SecureData[key] = value;
        return this;
    }

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        throw new NotSupportedException(
            "ResourceEvent must be translated by a host-specific result executor (e.g. HttpOperationResultExecutor) instead of self-executing.");
    }
}

/// <summary>
/// A typed subclass of <see cref="ResourceEvent"/> that provides strongly-typed access to the
/// resource payload via <see cref="Data"/>.
/// </summary>
/// <typeparam name="TResource">The type of API resource contained within the event.</typeparam>
public class ResourceEvent<TResource> : ResourceEvent
    where TResource : ApiResource
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceEvent{TResource}"/> class with
    /// a self-query that loads the associated resource.
    /// </summary>
    /// <param name="changeType">The type of change.</param>
    /// <param name="eventId">The specific event type.</param>
    /// <param name="selfQuery">An operation that, when executed, returns the resource.</param>
    public ResourceEvent(
        ResourceEventChangeType changeType,
        string eventId,
        IQuery selfQuery)
        : base(changeType, eventId, typeof(TResource), selfQuery)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceEvent{TResource}"/> class with an
    /// inline resource body.
    /// </summary>
    /// <param name="changeType">The type of change.</param>
    /// <param name="eventId">The specific event type.</param>
    /// <param name="data">The resource this event is for.</param>
    [JsonConstructor]
    public ResourceEvent(
        ResourceEventChangeType changeType,
        string eventId,
        TResource data)
        : base(changeType, eventId, data)
    {
    }

    /// <summary>
    /// Gets or sets the strongly-typed resource payload.
    /// </summary>
    [JsonIgnore]
    public new TResource? Data
    {
        get => (TResource?)base.Data;
        set => base.Data = value;
    }

    /// <summary>
    /// Creates a fully-qualified event id by combining the resource type name with the given sub-id.
    /// For example <c>CreateId("created")</c> on a <c>UserApiResource</c> produces <c>"user.created"</c>.
    /// </summary>
    /// <param name="eventSubId">The sub-ID unique within this resource's event namespace.</param>
    /// <returns>A fully-qualified event id.</returns>
    public static string CreateId(string eventSubId)
    {
        return GetResourceTypeName(typeof(TResource)) + "." + eventSubId;
    }

    /// <summary>
    /// Adds the given key-value pair to this event's <see cref="ResourceEvent.Metadata"/> dictionary.
    /// </summary>
    /// <param name="key">The key of the item to add.</param>
    /// <param name="value">The value of the item to add.</param>
    /// <returns>This <see cref="ResourceEvent{TResource}"/> to support further chaining.</returns>
    public new ResourceEvent<TResource> WithMetadata(string key, object value)
    {
        base.WithMetadata(key, value);
        return this;
    }

    /// <summary>
    /// Adds the given key-value pair to this event's <see cref="ResourceEvent.SecureData"/> dictionary.
    /// </summary>
    /// <param name="key">The key of the item to add.</param>
    /// <param name="value">The value of the item to add.</param>
    /// <returns>This <see cref="ResourceEvent{TResource}"/> to support further chaining.</returns>
    public new ResourceEvent<TResource> WithSecureData(string key, object value)
    {
        base.WithSecureData(key, value);
        return this;
    }
}

/// <summary>
/// A <see cref="ResourceEvent{TResource}"/> pre-configured with change type
/// <see cref="ResourceEventChangeType.Created"/>.
/// </summary>
/// <typeparam name="TResource">The type of API resource contained within the event.</typeparam>
public class ResourceCreated<TResource> : ResourceEvent<TResource>
    where TResource : ApiResource
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceCreated{TResource}"/> class with
    /// the default event sub-id of <c>created</c>.
    /// </summary>
    /// <param name="selfQuery">An operation that, when executed, returns the resource.</param>
    public ResourceCreated(IQuery selfQuery)
        : base(ResourceEventChangeType.Created, CreateId("created"), selfQuery)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceCreated{TResource}"/> class with
    /// a custom event sub-id.
    /// </summary>
    /// <param name="eventSubId">The event sub-id (e.g. <c>"signed-up"</c>).</param>
    /// <param name="selfQuery">An operation that, when executed, returns the resource.</param>
    public ResourceCreated(string eventSubId, IQuery selfQuery)
        : base(ResourceEventChangeType.Created, CreateId(eventSubId), selfQuery)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceCreated{TResource}"/> class with an inline
    /// resource body and the default event sub-id.
    /// </summary>
    /// <param name="data">The resource this event is for.</param>
    public ResourceCreated(TResource data)
        : base(ResourceEventChangeType.Created, CreateId("created"), data)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceCreated{TResource}"/> class with an inline
    /// resource body and a custom event sub-id.
    /// </summary>
    /// <param name="eventSubId">The event sub-id.</param>
    /// <param name="data">The resource this event is for.</param>
    [JsonConstructor]
    public ResourceCreated(string eventSubId, TResource data)
        : base(ResourceEventChangeType.Created, CreateId(eventSubId), data)
    {
    }
}

/// <summary>
/// A <see cref="ResourceEvent{TResource}"/> pre-configured with change type
/// <see cref="ResourceEventChangeType.Updated"/>.
/// </summary>
/// <typeparam name="TResource">The type of API resource contained within the event.</typeparam>
public class ResourceUpdated<TResource> : ResourceEvent<TResource>
    where TResource : ApiResource
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceUpdated{TResource}"/> class with
    /// the default event sub-id of <c>updated</c>.
    /// </summary>
    /// <param name="selfQuery">An operation that, when executed, returns the resource.</param>
    public ResourceUpdated(IQuery selfQuery)
        : base(ResourceEventChangeType.Updated, CreateId("updated"), selfQuery)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceUpdated{TResource}"/> class with
    /// a custom event sub-id.
    /// </summary>
    /// <param name="eventSubId">The event sub-id.</param>
    /// <param name="selfQuery">An operation that, when executed, returns the resource.</param>
    public ResourceUpdated(string eventSubId, IQuery selfQuery)
        : base(ResourceEventChangeType.Updated, CreateId(eventSubId), selfQuery)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceUpdated{TResource}"/> class with an inline
    /// resource body and the default event sub-id.
    /// </summary>
    /// <param name="data">The resource this event is for.</param>
    public ResourceUpdated(TResource data)
        : base(ResourceEventChangeType.Updated, CreateId("updated"), data)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceUpdated{TResource}"/> class with an inline
    /// resource body and a custom event sub-id.
    /// </summary>
    /// <param name="eventSubId">The event sub-id.</param>
    /// <param name="data">The resource this event is for.</param>
    [JsonConstructor]
    public ResourceUpdated(string eventSubId, TResource data)
        : base(ResourceEventChangeType.Updated, CreateId(eventSubId), data)
    {
    }
}

/// <summary>
/// A <see cref="ResourceEvent{TResource}"/> pre-configured with change type
/// <see cref="ResourceEventChangeType.Deleted"/>.
/// </summary>
/// <typeparam name="TResource">The type of API resource contained within the event.</typeparam>
public class ResourceDeleted<TResource> : ResourceEvent<TResource>
    where TResource : ApiResource
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceDeleted{TResource}"/> class with
    /// the default event sub-id of <c>deleted</c>.
    /// </summary>
    /// <param name="selfQuery">An operation that, when executed, returns the resource.</param>
    public ResourceDeleted(IQuery selfQuery)
        : base(ResourceEventChangeType.Deleted, CreateId("deleted"), selfQuery)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceDeleted{TResource}"/> class with
    /// a custom event sub-id.
    /// </summary>
    /// <param name="eventSubId">The event sub-id.</param>
    /// <param name="selfQuery">An operation that, when executed, returns the resource.</param>
    public ResourceDeleted(string eventSubId, IQuery selfQuery)
        : base(ResourceEventChangeType.Deleted, CreateId(eventSubId), selfQuery)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceDeleted{TResource}"/> class with an inline
    /// resource body and the default event sub-id.
    /// </summary>
    /// <param name="data">The resource this event is for.</param>
    public ResourceDeleted(TResource data)
        : base(ResourceEventChangeType.Deleted, CreateId("deleted"), data)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceDeleted{TResource}"/> class with an inline
    /// resource body and a custom event sub-id.
    /// </summary>
    /// <param name="eventSubId">The event sub-id.</param>
    /// <param name="data">The resource this event is for.</param>
    [JsonConstructor]
    public ResourceDeleted(string eventSubId, TResource data)
        : base(ResourceEventChangeType.Deleted, CreateId(eventSubId), data)
    {
    }
}

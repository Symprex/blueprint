using Blueprint.Model;

namespace Blueprint.Runtime.Exec;

/// <summary>
/// A convenience specialization of <see cref="ResourceEventResult{T}"/> for resource creation events.
/// Handlers return this to signal that a new resource has been created, which the HTTP host translates
/// into a 201 Created response with an appropriate <c>Location</c> header.
/// </summary>
/// <typeparam name="T">The type of resource that was created.</typeparam>
public sealed class ResourceCreated<T> : ResourceEventResult<T> where T : notnull
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceCreated{T}"/> class with an explicit event ID.
    /// </summary>
    /// <param name="eventId">The specific event identifier (e.g. "account.created").</param>
    /// <param name="resourceId">The stable resource identity for the newly created resource.</param>
    /// <param name="content">The created resource payload.</param>
    public ResourceCreated(string eventId, string resourceId, T content)
        : base(ChangeType.Created, eventId, resourceId, content)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceCreated{T}"/> class with a conventional
    /// event ID derived from the resource type name.
    /// </summary>
    /// <param name="resourceId">The stable resource identity for the newly created resource.</param>
    /// <param name="content">The created resource payload.</param>
    public ResourceCreated(string resourceId, T content)
        : base(ChangeType.Created, typeof(T).Name + ".created", resourceId, content)
    {
    }
}

/// <summary>
/// A convenience specialization of <see cref="ResourceEventResult{T}"/> for resource update events.
/// Handlers return this to signal that an existing resource has been modified, which the HTTP host
/// translates into a 200 OK response.
/// </summary>
/// <typeparam name="T">The type of resource that was updated.</typeparam>
public sealed class ResourceUpdated<T> : ResourceEventResult<T> where T : notnull
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceUpdated{T}"/> class with an explicit event ID.
    /// </summary>
    /// <param name="eventId">The specific event identifier (e.g. "account.settings.changed").</param>
    /// <param name="resourceId">The stable resource identity for the updated resource.</param>
    /// <param name="content">The updated resource payload.</param>
    public ResourceUpdated(string eventId, string resourceId, T content)
        : base(ChangeType.Updated, eventId, resourceId, content)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceUpdated{T}"/> class with a conventional
    /// event ID derived from the resource type name.
    /// </summary>
    /// <param name="resourceId">The stable resource identity for the updated resource.</param>
    /// <param name="content">The updated resource payload.</param>
    public ResourceUpdated(string resourceId, T content)
        : base(ChangeType.Updated, typeof(T).Name + ".updated", resourceId, content)
    {
    }
}

/// <summary>
/// A convenience specialization of <see cref="ResourceEventResult{T}"/> for resource deletion events.
/// Handlers return this to signal that a resource has been removed, which the HTTP host translates
/// into a 200 OK response (with the deleted resource as body) or 204 No Content.
/// </summary>
/// <typeparam name="T">The type of resource that was deleted.</typeparam>
public sealed class ResourceDeleted<T> : ResourceEventResult<T> where T : notnull
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceDeleted{T}"/> class with an explicit event ID.
    /// </summary>
    /// <param name="eventId">The specific event identifier (e.g. "account.deleted").</param>
    /// <param name="resourceId">The stable resource identity for the deleted resource.</param>
    /// <param name="content">The deleted resource's final state payload.</param>
    public ResourceDeleted(string eventId, string resourceId, T content)
        : base(ChangeType.Deleted, eventId, resourceId, content)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceDeleted{T}"/> class with a conventional
    /// event ID derived from the resource type name.
    /// </summary>
    /// <param name="resourceId">The stable resource identity for the deleted resource.</param>
    /// <param name="content">The deleted resource's final state payload.</param>
    public ResourceDeleted(string resourceId, T content)
        : base(ChangeType.Deleted, typeof(T).Name + ".deleted", resourceId, content)
    {
    }
}

using Blueprint.Model;

namespace Blueprint.Http;

/// <summary>
/// Represents a unique event type within a system. Instances are typically held as
/// <c>static readonly</c> fields on a per-resource events class, serving as both
/// documentation of known event types and factories for creating <see cref="ResourceEvent{TResource}"/>
/// instances at runtime.
/// </summary>
/// <example>
/// <code>
/// public static class TenantEvents
/// {
///     private static readonly ResourceEventDefinitionFactory&lt;TenantApiResource&gt; Factory =
///         ResourceEventDefinition.For&lt;TenantApiResource&gt;();
///
///     public static readonly ResourceEventDefinition&lt;TenantApiResource&gt; Created = Factory.Created();
///     public static readonly ResourceEventDefinition&lt;TenantApiResource&gt; Updated = Factory.Updated("modified");
/// }
/// </code>
/// </example>
/// <typeparam name="TResource">The type of resource that has been modified.</typeparam>
public class ResourceEventDefinition<TResource>
    where TResource : ApiResource
{
    /// <summary>
    /// Gets the change type of events produced by this definition.
    /// </summary>
    public ResourceEventChangeType ChangeType { get; }

    /// <summary>
    /// The fully-qualified event id (e.g. <c>"tenant.created"</c>), accessible to derived definitions
    /// that need direct access when constructing <see cref="ResourceEvent{TResource}"/> instances.
    /// </summary>
#pragma warning disable SA1401 // Field should be private — private protected access is intentional for perf
    private protected readonly string EventId;
#pragma warning restore SA1401

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceEventDefinition{TResource}"/> class.
    /// </summary>
    /// <param name="changeType">The type of change.</param>
    /// <param name="eventId">The fully-qualified event id.</param>
    public ResourceEventDefinition(ResourceEventChangeType changeType, string eventId)
    {
        this.ChangeType = changeType;
        this.EventId = eventId;
    }

    /// <summary>
    /// Creates a new <see cref="ResourceEvent{TResource}"/> from this definition with an inline
    /// resource body.
    /// </summary>
    /// <param name="resource">The resource this event is for.</param>
    /// <param name="metadata">Optional metadata attached to the created event.</param>
    /// <returns>A new resource event.</returns>
    public ResourceEvent<TResource> New(TResource resource, IDictionary<string, object>? metadata = null)
    {
        var resourceEvent = new ResourceEvent<TResource>(this.ChangeType, this.EventId, resource);
        ApplyMetadata(resourceEvent, metadata);
        return resourceEvent;
    }

    /// <summary>
    /// Checks whether the given <see cref="ResourceEvent"/> matches events that would be created by
    /// this definition (same change type, event id, and resource object name).
    /// </summary>
    /// <param name="event">The event to check.</param>
    /// <returns>Whether the event matches.</returns>
    public bool Matches(ResourceEvent @event)
    {
        return @event.ChangeType == this.ChangeType &&
               @event.EventId == this.EventId &&
               @event.ResourceObject == ResourceEvent.GetResourceTypeName(typeof(TResource));
    }

    /// <summary>
    /// Applies optional metadata to a resource event.
    /// </summary>
    /// <param name="resourceEvent">The event to apply metadata to.</param>
    /// <param name="metadata">Optional metadata dictionary; null is a safe no-op.</param>
    private protected static void ApplyMetadata(ResourceEvent resourceEvent, IDictionary<string, object>? metadata)
    {
        if (metadata != null)
        {
            foreach (var kvp in metadata)
            {
                resourceEvent.WithMetadata(kvp.Key, kvp.Value);
            }
        }
    }
}

/// <summary>
/// A <see cref="ResourceEventDefinition{TResource}"/> variant that carries a parameterless mapper,
/// enabling <see cref="New"/> to be called without supplying a resource instance — the mapper
/// constructs the resource or self-query automatically.
/// </summary>
/// <typeparam name="TResource">The type of resource that has been modified.</typeparam>
public class ResourceEventDefinitionWithMapper<TResource> : ResourceEventDefinition<TResource>
    where TResource : ApiResource
{
    private readonly Func<TResource>? _mapper;
    private readonly Func<object>? _queryMapper;

    /// <summary>
    /// Initializes a new instance with a query-producing mapper.
    /// </summary>
    /// <param name="changeType">The type of change.</param>
    /// <param name="eventId">The fully-qualified event id.</param>
    /// <param name="queryMapper">A mapper to construct a self-query operation.</param>
    public ResourceEventDefinitionWithMapper(
        ResourceEventChangeType changeType,
        string eventId,
        Func<object> queryMapper)
        : base(changeType, eventId)
    {
        this._queryMapper = queryMapper;
    }

    /// <summary>
    /// Initializes a new instance with a resource-producing mapper.
    /// </summary>
    /// <param name="changeType">The type of change.</param>
    /// <param name="eventId">The fully-qualified event id.</param>
    /// <param name="mapper">A mapper to construct the final resource.</param>
    public ResourceEventDefinitionWithMapper(
        ResourceEventChangeType changeType,
        string eventId,
        Func<TResource> mapper)
        : base(changeType, eventId)
    {
        this._mapper = mapper;
    }

    /// <summary>
    /// Creates a new <see cref="ResourceEvent{TResource}"/> by invoking this definition's mapper.
    /// </summary>
    /// <param name="metadata">Optional metadata attached to the created event.</param>
    /// <returns>A new resource event.</returns>
    public ResourceEvent<TResource> New(IDictionary<string, object>? metadata = null)
    {
        var resourceEvent = this._mapper != null
            ? new ResourceEvent<TResource>(this.ChangeType, this.EventId, this._mapper())
            : new ResourceEvent<TResource>(this.ChangeType, this.EventId, (IQuery)this._queryMapper!());

        ApplyMetadata(resourceEvent, metadata);
        return resourceEvent;
    }
}

/// <summary>
/// A <see cref="ResourceEventDefinition{TResource}"/> variant that carries a domain-object mapper,
/// enabling <see cref="New"/> to transform a domain entity into the appropriate resource or
/// self-query without the caller needing to do the mapping.
/// </summary>
/// <typeparam name="TResource">The type of resource that has been modified.</typeparam>
/// <typeparam name="TDomain">A domain type that will be passed to the mapper when creating an instance.</typeparam>
public sealed class ResourceEventDefinitionWithMapper<TResource, TDomain> : ResourceEventDefinition<TResource>
    where TResource : ApiResource
{
    private readonly Func<TDomain, TResource>? _mapper;
    private readonly Func<TDomain, object>? _queryMapper;

    /// <summary>
    /// Initializes a new instance with a query-producing mapper.
    /// </summary>
    /// <param name="changeType">The type of change.</param>
    /// <param name="eventId">The fully-qualified event id.</param>
    /// <param name="queryMapper">A mapper that constructs a self-query from a domain object.</param>
    public ResourceEventDefinitionWithMapper(
        ResourceEventChangeType changeType,
        string eventId,
        Func<TDomain, object> queryMapper)
        : base(changeType, eventId)
    {
        this._queryMapper = queryMapper;
    }

    /// <summary>
    /// Initializes a new instance with a resource-producing mapper.
    /// </summary>
    /// <param name="changeType">The type of change.</param>
    /// <param name="eventId">The fully-qualified event id.</param>
    /// <param name="mapper">A mapper that constructs the final resource from a domain object.</param>
    public ResourceEventDefinitionWithMapper(
        ResourceEventChangeType changeType,
        string eventId,
        Func<TDomain, TResource> mapper)
        : base(changeType, eventId)
    {
        this._mapper = mapper;
    }

    /// <summary>
    /// Creates a new <see cref="ResourceEvent{TResource}"/> by passing the domain object to this
    /// definition's mapper.
    /// </summary>
    /// <param name="domainObject">The domain object to pass to the mapper.</param>
    /// <param name="metadata">Optional metadata attached to the created event.</param>
    /// <returns>A new resource event.</returns>
    public ResourceEvent<TResource> New(TDomain domainObject, IDictionary<string, object>? metadata = null)
    {
        var resourceEvent = this._mapper != null
            ? new ResourceEvent<TResource>(this.ChangeType, this.EventId, this._mapper(domainObject))
            : new ResourceEvent<TResource>(this.ChangeType, this.EventId, (IQuery)this._queryMapper!(domainObject));

        ApplyMetadata(resourceEvent, metadata);
        return resourceEvent;
    }
}

/// <summary>
/// Static entry point for creating <see cref="ResourceEventDefinitionFactory{TResource}"/> and
/// <see cref="ResourceEventDefinitionFactoryWithMapper{TResource, TDomain}"/> instances.
/// </summary>
public static class ResourceEventDefinition
{
    /// <summary>
    /// Creates a simple factory that produces <see cref="ResourceEventDefinition{TResource}"/> instances
    /// requiring the caller to supply the resource body at event-creation time.
    /// </summary>
    /// <typeparam name="TResource">The API resource type.</typeparam>
    /// <returns>A factory for the given resource type.</returns>
    public static ResourceEventDefinitionFactory<TResource> For<TResource>()
        where TResource : ApiResource
    {
        return new ResourceEventDefinitionFactory<TResource>();
    }

    /// <summary>
    /// Creates a factory that produces mapper-backed definitions using a parameterless query mapper.
    /// The mapper is inherited by every definition the factory creates.
    /// </summary>
    /// <typeparam name="TResource">The API resource type.</typeparam>
    /// <param name="queryMapper">A mapper to construct a self-query operation.</param>
    /// <returns>A factory that produces mapper-backed definitions.</returns>
    public static ResourceEventDefinitionFactoryWithMapper<TResource> For<TResource>(
        Func<object> queryMapper)
        where TResource : ApiResource
    {
        return new ResourceEventDefinitionFactoryWithMapper<TResource>(queryMapper);
    }

    /// <summary>
    /// Creates a factory that produces mapper-backed definitions using a parameterless resource mapper.
    /// The mapper is inherited by every definition the factory creates.
    /// </summary>
    /// <typeparam name="TResource">The API resource type.</typeparam>
    /// <param name="mapper">A mapper to construct the resource directly.</param>
    /// <returns>A factory that produces mapper-backed definitions.</returns>
    public static ResourceEventDefinitionFactoryWithMapper<TResource> For<TResource>(
        Func<TResource> mapper)
        where TResource : ApiResource
    {
        return new ResourceEventDefinitionFactoryWithMapper<TResource>(mapper);
    }

    /// <summary>
    /// Creates a factory that produces mapper-backed definitions using a fixed query instance.
    /// Every definition the factory creates will use this query as the self-query for events.
    /// </summary>
    /// <typeparam name="TResource">The API resource type.</typeparam>
    /// <param name="query">An existing query instance to use as the self-query.</param>
    /// <returns>A factory that produces mapper-backed definitions.</returns>
    public static ResourceEventDefinitionFactoryWithMapper<TResource> For<TResource>(
        IQuery query)
        where TResource : ApiResource
    {
        return new ResourceEventDefinitionFactoryWithMapper<TResource>(() => query);
    }

    /// <summary>
    /// Creates a factory that produces mapper-backed definitions using a fixed typed query instance.
    /// This overload allows type inference when the query's result type matches the resource type
    /// (e.g. <c>ResourceEventDefinition.For(new GetSettingsQuery())</c> where <c>GetSettingsQuery : IQuery&lt;SettingsApiResource&gt;</c>).
    /// </summary>
    /// <typeparam name="TResource">The API resource type, inferred from the query's result type.</typeparam>
    /// <param name="query">An existing query instance to use as the self-query.</param>
    /// <returns>A factory that produces mapper-backed definitions.</returns>
    public static ResourceEventDefinitionFactoryWithMapper<TResource> For<TResource>(
        IQuery<TResource> query)
        where TResource : ApiResource
    {
        return new ResourceEventDefinitionFactoryWithMapper<TResource>(() => query);
    }

    /// <summary>
    /// Creates a factory that produces domain-mapper definitions using a domain-to-query mapper.
    /// The mapper is inherited by every definition the factory creates.
    /// </summary>
    /// <typeparam name="TResource">The API resource type.</typeparam>
    /// <typeparam name="TDomain">The domain entity type.</typeparam>
    /// <param name="queryMapper">A mapper that constructs a self-query from a domain object.</param>
    /// <returns>A factory that produces domain-mapper definitions.</returns>
    public static ResourceEventDefinitionFactoryWithMapper<TResource, TDomain> For<TResource, TDomain>(
        Func<TDomain, object> queryMapper)
        where TResource : ApiResource
    {
        return new ResourceEventDefinitionFactoryWithMapper<TResource, TDomain>(queryMapper);
    }

    /// <summary>
    /// Creates a factory that produces domain-mapper definitions using a domain-to-resource mapper.
    /// The mapper is inherited by every definition the factory creates.
    /// </summary>
    /// <typeparam name="TResource">The API resource type.</typeparam>
    /// <typeparam name="TDomain">The domain entity type.</typeparam>
    /// <param name="mapper">A mapper that constructs the resource from a domain object.</param>
    /// <returns>A factory that produces domain-mapper definitions.</returns>
    public static ResourceEventDefinitionFactoryWithMapper<TResource, TDomain> For<TResource, TDomain>(
        Func<TDomain, TResource> mapper)
        where TResource : ApiResource
    {
        return new ResourceEventDefinitionFactoryWithMapper<TResource, TDomain>(mapper);
    }
}

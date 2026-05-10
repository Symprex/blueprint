using Blueprint.Model;

namespace Blueprint.Http;

/// <summary>
/// Base class for factories that produce <see cref="ResourceEventDefinition{TResource}"/> variants.
/// Provides convenience methods for the three standard change types (created, updated, deleted),
/// each with both default and custom sub-id overloads.
/// </summary>
/// <typeparam name="TResource">The API resource type.</typeparam>
/// <typeparam name="TReturn">The concrete definition type produced by this factory.</typeparam>
public abstract class ResourceEventDefinitionFactoryBase<TResource, TReturn>
    where TResource : ApiResource
{
    /// <summary>
    /// Creates a definition with change type <see cref="ResourceEventChangeType.Created"/>
    /// and the default sub-id <c>created</c>.
    /// </summary>
    /// <returns>A new resource event definition.</returns>
    public TReturn Created()
    {
        return this.Create(
            ResourceEventChangeType.Created,
            ResourceEvent<TResource>.CreateId("created"));
    }

    /// <summary>
    /// Creates a definition with change type <see cref="ResourceEventChangeType.Created"/>
    /// and the given sub-id.
    /// </summary>
    /// <param name="eventSubId">The sub-ID unique within this resource's event namespace.</param>
    /// <returns>A new resource event definition.</returns>
    public TReturn Created(string eventSubId)
    {
        return this.Create(
            ResourceEventChangeType.Created,
            ResourceEvent<TResource>.CreateId(eventSubId));
    }

    /// <summary>
    /// Creates a definition with change type <see cref="ResourceEventChangeType.Updated"/>
    /// and the default sub-id <c>updated</c>.
    /// </summary>
    /// <returns>A new resource event definition.</returns>
    public TReturn Updated()
    {
        return this.Create(
            ResourceEventChangeType.Updated,
            ResourceEvent<TResource>.CreateId("updated"));
    }

    /// <summary>
    /// Creates a definition with change type <see cref="ResourceEventChangeType.Updated"/>
    /// and the given sub-id.
    /// </summary>
    /// <param name="eventSubId">The sub-ID unique within this resource's event namespace.</param>
    /// <returns>A new resource event definition.</returns>
    public TReturn Updated(string eventSubId)
    {
        return this.Create(
            ResourceEventChangeType.Updated,
            ResourceEvent<TResource>.CreateId(eventSubId));
    }

    /// <summary>
    /// Creates a definition with change type <see cref="ResourceEventChangeType.Deleted"/>
    /// and the default sub-id <c>deleted</c>.
    /// </summary>
    /// <returns>A new resource event definition.</returns>
    public TReturn Deleted()
    {
        return this.Create(
            ResourceEventChangeType.Deleted,
            ResourceEvent<TResource>.CreateId("deleted"));
    }

    /// <summary>
    /// Creates a definition with change type <see cref="ResourceEventChangeType.Deleted"/>
    /// and the given sub-id.
    /// </summary>
    /// <param name="eventSubId">The sub-ID unique within this resource's event namespace.</param>
    /// <returns>A new resource event definition.</returns>
    public TReturn Deleted(string eventSubId)
    {
        return this.Create(
            ResourceEventChangeType.Deleted,
            ResourceEvent<TResource>.CreateId(eventSubId));
    }

    /// <summary>
    /// Creates the concrete definition instance from this factory.
    /// </summary>
    /// <param name="type">The change type.</param>
    /// <param name="id">The fully-qualified event id.</param>
    /// <returns>A definition for the given arguments.</returns>
    protected abstract TReturn Create(ResourceEventChangeType type, string id);
}

/// <summary>
/// A factory that produces simple <see cref="ResourceEventDefinition{TResource}"/> instances
/// requiring the caller to supply the resource body when calling <see cref="ResourceEventDefinition{TResource}.New"/>.
/// </summary>
/// <typeparam name="TResource">The type of resource that has been modified.</typeparam>
public sealed class ResourceEventDefinitionFactory<TResource>
    : ResourceEventDefinitionFactoryBase<TResource, ResourceEventDefinition<TResource>>
    where TResource : ApiResource
{
    /// <inheritdoc/>
    protected override ResourceEventDefinition<TResource> Create(ResourceEventChangeType type, string id)
    {
        return new ResourceEventDefinition<TResource>(type, id);
    }
}

/// <summary>
/// A factory that produces <see cref="ResourceEventDefinitionWithMapper{TResource}"/> instances,
/// inheriting a parameterless mapper so callers can create events without supplying a resource or query.
/// </summary>
/// <typeparam name="TResource">The type of resource that has been modified.</typeparam>
public sealed class ResourceEventDefinitionFactoryWithMapper<TResource>
    : ResourceEventDefinitionFactoryBase<TResource, ResourceEventDefinitionWithMapper<TResource>>
    where TResource : ApiResource
{
    private readonly Func<TResource>? _mapper;
    private readonly Func<object>? _queryMapper;

    /// <summary>
    /// Initializes a new instance with a query-producing mapper.
    /// </summary>
    /// <param name="queryMapper">A mapper to construct a self-query operation.</param>
    public ResourceEventDefinitionFactoryWithMapper(Func<object> queryMapper)
    {
        this._queryMapper = queryMapper;
    }

    /// <summary>
    /// Initializes a new instance with a resource-producing mapper.
    /// </summary>
    /// <param name="mapper">A mapper to construct the final resource.</param>
    public ResourceEventDefinitionFactoryWithMapper(Func<TResource> mapper)
    {
        this._mapper = mapper;
    }

    /// <inheritdoc/>
    protected override ResourceEventDefinitionWithMapper<TResource> Create(ResourceEventChangeType type, string id)
    {
        return this._mapper != null
            ? new ResourceEventDefinitionWithMapper<TResource>(type, id, this._mapper)
            : new ResourceEventDefinitionWithMapper<TResource>(type, id, this._queryMapper!);
    }
}

/// <summary>
/// A factory that produces <see cref="ResourceEventDefinitionWithMapper{TResource, TDomain}"/> instances,
/// inheriting a domain-to-resource (or domain-to-query) mapper so callers can create events by passing
/// the domain entity directly.
/// </summary>
/// <typeparam name="TResource">The type of resource that has been modified.</typeparam>
/// <typeparam name="TDomain">The domain entity type that the mapper transforms.</typeparam>
public sealed class ResourceEventDefinitionFactoryWithMapper<TResource, TDomain>
    : ResourceEventDefinitionFactoryBase<TResource, ResourceEventDefinitionWithMapper<TResource, TDomain>>
    where TResource : ApiResource
{
    private readonly Func<TDomain, TResource>? _mapper;
    private readonly Func<TDomain, object>? _queryMapper;

    /// <summary>
    /// Initializes a new instance with a domain-to-query mapper.
    /// </summary>
    /// <param name="queryMapper">A mapper that constructs a self-query from a domain object.</param>
    public ResourceEventDefinitionFactoryWithMapper(Func<TDomain, object> queryMapper)
    {
        this._queryMapper = queryMapper;
    }

    /// <summary>
    /// Initializes a new instance with a domain-to-resource mapper.
    /// </summary>
    /// <param name="mapper">A mapper that constructs the resource from a domain object.</param>
    public ResourceEventDefinitionFactoryWithMapper(Func<TDomain, TResource> mapper)
    {
        this._mapper = mapper;
    }

    /// <inheritdoc/>
    protected override ResourceEventDefinitionWithMapper<TResource, TDomain> Create(
        ResourceEventChangeType type,
        string id)
    {
        return this._mapper != null
            ? new ResourceEventDefinitionWithMapper<TResource, TDomain>(type, id, this._mapper)
            : new ResourceEventDefinitionWithMapper<TResource, TDomain>(type, id, this._queryMapper!);
    }
}

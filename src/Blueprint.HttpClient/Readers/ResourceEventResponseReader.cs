using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using Blueprint.Http;
using Blueprint.Model;

namespace Blueprint.HttpClient.Readers;

/// <summary>
/// Reads a response declared as a <see cref="ResourceEvent{TResource}"/>, or a generic subclass of one such as
/// <see cref="ResourceCreated{TResource}"/>, from the event the host wrote, by name rather than by constructor, as legacy
/// Blueprint's client read one.
/// </summary>
/// <remarks>
/// <para>
/// System.Text.Json cannot bind these types' constructors, so each read makes a new <typeparamref name="TEvent"/> itself
/// and sets on it each member the wire carries by its exact name: a member the wire does not carry is left unset, as on the
/// uninitialised instance legacy's converter read one into, and an absent or <see langword="null"/> <c>metadata</c> or
/// <c>secureData</c> is an empty dictionary. A caller passes nothing to make it with.
/// </para>
/// <para>
/// How the event is made is chosen once, when the reader is constructed. <see cref="ResourceEvent{TResource}"/>,
/// <see cref="ResourceCreated{TResource}"/>, <see cref="ResourceUpdated{TResource}"/> and
/// <see cref="ResourceDeleted{TResource}"/> are made by an internal constructor that sets nothing, so a read of one is a
/// delegate call and no reflection. Any other <typeparamref name="TEvent"/>, a subclass declared outside Blueprint, has no
/// such constructor, so it takes the slow path: it is made uninitialised, as legacy's converter made every event, without
/// running a constructor of its own.
/// </para>
/// <para>
/// A JSON <c>null</c> is legacy's <c>response_empty</c> failure, and a 204 its "could not understand" failure. In a union
/// it is a 200 JSON body, told apart from an event of another resource by the <c>resourceObject</c> the host writes.
/// </para>
/// </remarks>
/// <typeparam name="TEvent">The declared event type.</typeparam>
/// <typeparam name="TResource">The resource the event carries.</typeparam>
public sealed class ResourceEventResponseReader<TEvent, TResource> : ResponseReader<TEvent>
    where TEvent : ResourceEvent<TResource>
    where TResource : ApiResource
{
    private readonly Func<TEvent> _create;
    private readonly JsonTypeInfo<TResource>? _dataTypeInfo;
    private readonly string _resourceObject;

    /// <summary>
    /// Initialises a new instance of the <see cref="ResourceEventResponseReader{TEvent, TResource}"/> class.
    /// </summary>
    /// <param name="dataTypeInfo">
    /// The metadata to read the event's data with, from the transport's options; or <see langword="null"/> to read it with
    /// the transport's options' own.
    /// </param>
    /// <exception cref="ArgumentException"><typeparamref name="TEvent"/> is abstract, so no event of it can be made.</exception>
    public ResourceEventResponseReader(JsonTypeInfo<TResource>? dataTypeInfo = null)
    {
        // Refused here rather than by every read, which could never make one.
        if (typeof(TEvent).IsAbstract)
        {
            throw new ArgumentException($"A resource event reader makes the {typeof(TEvent)} it reads, which an abstract type cannot be.");
        }

        this._create = BindCreate();
        this._dataTypeInfo = dataTypeInfo;
        this._resourceObject = ApiResource.GetTypeName(typeof(TResource));
    }

    /// <inheritdoc />
    internal override string? EventResourceObject => this._resourceObject;

    /// <inheritdoc />
    internal override async ValueTask<TEvent> ReadAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        if (context.Status == (int)HttpStatusCode.NoContent)
        {
            throw context.CreateNotUnderstoodException();
        }

        var resourceEvent = await ResourceEventReader.ReadOrNullAsync(this._create, context.Response.Content, context.JsonOptions, this._dataTypeInfo, cancellationToken).ConfigureAwait(false);

        // A JSON null is no event.
        return resourceEvent ?? throw await context.CreateResponseEmptyExceptionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    internal override bool IsJsonBodyOfStatus(int status)
    {
        // The host writes a legacy resource event as a 200 whatever its change type, as legacy's OkResult sent one.
        return status == (int)HttpStatusCode.OK;
    }

    /// <inheritdoc />
    internal override async ValueTask<(bool Read, TEvent Value)> ReadJsonMemberAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        return (true, await ResourceEventReader.ReadAsync(this._create, context.Response.Content, context.JsonOptions, this._dataTypeInfo, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Chooses how each read makes its <typeparamref name="TEvent"/>: by Blueprint's internal constructor that sets nothing
    /// for its own four event types, and otherwise, the slow path, uninitialised.
    /// </summary>
    private static Func<TEvent> BindCreate()
    {
        if (typeof(TEvent) == typeof(ResourceCreated<TResource>))
        {
            return static () => (TEvent)(object)new ResourceCreated<TResource>();
        }

        if (typeof(TEvent) == typeof(ResourceUpdated<TResource>))
        {
            return static () => (TEvent)(object)new ResourceUpdated<TResource>();
        }

        if (typeof(TEvent) == typeof(ResourceDeleted<TResource>))
        {
            return static () => (TEvent)(object)new ResourceDeleted<TResource>();
        }

        if (typeof(TEvent) == typeof(ResourceEvent<TResource>))
        {
            return static () => (TEvent)(object)new ResourceEvent<TResource>();
        }

        // A subclass declared outside Blueprint: none of its constructors can be relied on to set nothing, and this assembly
        // can call none that takes no data, so it is made as legacy's converter made every event.
        return static () => (TEvent)RuntimeHelpers.GetUninitializedObject(typeof(TEvent));
    }
}

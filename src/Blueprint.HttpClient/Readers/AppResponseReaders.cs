using System.Net;
using System.Reflection;
using System.Text.Json;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.HttpClient.Readers;

/// <summary>
/// Chooses the reader <see cref="BlueprintHttpClient{TApp}"/> reads a response type with: the run-time form of the choice
/// a generated typed client makes at compile time (see <see cref="ResponseReader{T}"/>), made once per response type.
/// </summary>
/// <remarks>
/// This is the one place the app-driven client reflects over a response type, to construct its reader; the reader then
/// reads every response of that type without it. The order of the tests is the order the client tested a response type in
/// before its readers existed, so each type is read as it was.
/// </remarks>
internal static class AppResponseReaders
{
    /// <summary>
    /// Gets the reader of <typeparamref name="T"/>, constructing it on first use.
    /// </summary>
    /// <typeparam name="T">The response type the caller asked for.</typeparam>
    /// <returns>The reader, shared by every call for <typeparamref name="T"/>: readers hold no per-call or per-client state.</returns>
    internal static ResponseReader<T> For<T>()
    {
        return Cache<T>.Reader;
    }

    private static object Create(Type type)
    {
        // A nested status-only result is matched by its own singleton before anything else is considered.
        if (typeof(StatusCodeResult).IsAssignableFrom(type) && type != typeof(StatusCodeResult))
        {
            return Make(nameof(CreateStatusCodeResultReader), type);
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Results<,>))
        {
            return Make(nameof(CreateUnionReader), type.GetGenericArguments());
        }

        if (type == typeof(StatusCodeResult) || type == typeof(HttpResult) || type == typeof(OperationResult) || type == typeof(object))
        {
            return Make(nameof(CreateBodilessResultReader), type);
        }

        if (type == typeof(PlainTextResult))
        {
            return new PlainTextResponseReader();
        }

        if (ResourceEventReader.CanRead(type))
        {
            return Make(nameof(CreateResourceEventReader), type, ResourceEventReader.FindResourceType(type)!);
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ResourceEventResult<>))
        {
            return Make(nameof(CreateResourceEventResultReader), type.GetGenericArguments());
        }

        if (type.IsGenericType
            && (type.GetGenericTypeDefinition() == typeof(OkResult<>)
                || type.GetGenericTypeDefinition() == typeof(CreatedResult<>)
                || type.GetGenericTypeDefinition() == typeof(AcceptedResult<>)))
        {
            return Make(nameof(CreateContentResultReader), type, type.GetGenericArguments()[0]);
        }

        return Make(nameof(CreateJsonReader), type);
    }

    private static object Make(string factoryName, params Type[] typeArguments)
    {
        return typeof(AppResponseReaders)
            .GetMethod(factoryName, BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeArguments)
            .Invoke(null, null)!;
    }

    private static StatusCodeResultResponseReader<T> CreateStatusCodeResultReader<T>()
        where T : StatusCodeResult
    {
        // As the client found the singleton before its readers existed: the type's own public static Instance field.
        var singleton = typeof(T).GetField(nameof(StatusCodeResult.Accepted.Instance), BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as StatusCodeResult;

        return StatusCodeResultResponseReader<T>.FromSingletonField(singleton);
    }

    private static UnionResponseReader<T1, T2> CreateUnionReader<T1, T2>()
        where T1 : notnull
        where T2 : notnull
    {
        return new UnionResponseReader<T1, T2>(For<T1>(), For<T2>());
    }

    private static BodilessResultResponseReader<T> CreateBodilessResultReader<T>()
    {
        return new BodilessResultResponseReader<T>();
    }

    private static ResourceEventResponseReader<TEvent, TResource> CreateResourceEventReader<TEvent, TResource>()
        where TEvent : ResourceEvent<TResource>
        where TResource : ApiResource
    {
        return new ResourceEventResponseReader<TEvent, TResource>();
    }

    private static ResourceEventResultResponseReader<T> CreateResourceEventResultReader<T>()
        where T : notnull
    {
        return new ResourceEventResultResponseReader<T>();
    }

    private static ContentResultResponseReader<TResult, TContent> CreateContentResultReader<TResult, TContent>()
        where TResult : OperationResult
        where TContent : notnull
    {
        return new ContentResultResponseReader<TResult, TContent>();
    }

    private static JsonResponseReader<T> CreateJsonReader<T>()
    {
        return new JsonResponseReader<T>();
    }

    /// <summary>
    /// Holds the reader of <typeparamref name="T"/>, made on first use. A failure to make one is not remembered, so it is
    /// raised again, as itself, by the next call rather than as a type initialiser's failure on every call after.
    /// </summary>
    private static class Cache<T>
    {
        private static ResponseReader<T>? _reader;

        internal static ResponseReader<T> Reader => LazyInitializer.EnsureInitialized(ref _reader, static () => (ResponseReader<T>)Create(typeof(T)));
    }
}

/// <summary>
/// Reads nothing of a success response: <see cref="BlueprintHttpClient{TApp}"/>'s send of an operation whose response it
/// only checks for success.
/// </summary>
internal sealed class NoResponseBodyReader : ResponseReader<object?>
{
    /// <summary>
    /// Gets the reader, which holds nothing.
    /// </summary>
    internal static NoResponseBodyReader Instance { get; } = new();

    /// <inheritdoc />
    internal override ValueTask<object?> ReadAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        return default;
    }
}

/// <summary>
/// Reads the resource out of the resource event a success response carries, for
/// <see cref="BlueprintHttpClient{TApp}.SendResourceEventAsync{TResource}(IOperation, CallOptions?, CancellationToken)"/>.
/// </summary>
/// <typeparam name="TResource">The resource type carried inside the event envelope.</typeparam>
internal sealed class ResourceEventDataReader<TResource> : ResponseReader<TResource?>
{
    /// <summary>
    /// Gets the reader, which holds nothing.
    /// </summary>
    internal static ResourceEventDataReader<TResource> Instance { get; } = new();

    /// <inheritdoc />
    internal override async ValueTask<TResource?> ReadAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        if (context.Response.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        // Parse the event envelope and extract the resource payload.
        // The envelope uses "data" for the content (matching ResourceEventEnvelope).
        using var doc = await JsonDocument.ParseAsync(
            await context.Response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (doc.RootElement.TryGetProperty("data", out var dataElement))
        {
            return dataElement.Deserialize<TResource>(context.JsonOptions);
        }

        // Legacy fallback: some event envelopes use "content" instead of "data"
        if (doc.RootElement.TryGetProperty("content", out var contentElement))
        {
            return contentElement.Deserialize<TResource>(context.JsonOptions);
        }

        // An event envelope with neither carries no resource: the host writes no data key for an event with none, such
        // as a deleted event, whose self query is never run, as legacy Blueprint wrote it. Read the envelope itself as
        // the resource and that would be a resource made of the envelope's members.
        if (doc.RootElement.TryGetProperty("$object", out var objectElement)
            && objectElement.ValueKind == JsonValueKind.String
            && objectElement.ValueEquals("event"))
        {
            return default;
        }

        // Fallback: try deserializing the whole body as TResource
        return doc.RootElement.Deserialize<TResource>(context.JsonOptions);
    }
}

/// <summary>
/// Reads the full resource event envelope a success response carries as a <see cref="ResourceEvent{TResource}"/>, for
/// <see cref="BlueprintHttpClient{TApp}.SendResourceEventEnvelopeAsync{TResource}(IOperation, CallOptions?, CancellationToken)"/>.
/// </summary>
/// <typeparam name="TResource">The resource type carried inside the envelope's <c>data</c> property.</typeparam>
internal sealed class ResourceEventEnvelopeReader<TResource> : ResponseReader<ResourceEvent<TResource>?>
    where TResource : ApiResource
{
    /// <summary>
    /// Gets the reader, which holds nothing.
    /// </summary>
    internal static ResourceEventEnvelopeReader<TResource> Instance { get; } = new();

    /// <summary>
    /// Parses a resource event JSON envelope into a fully-typed <see cref="ResourceEvent{TResource}"/>.
    /// The envelope shape is <c>{"$object":"event","eventId":"...","changeType":"...","data":{...},...}</c>
    /// as produced by <c>HttpOperationResultExecutor.WriteResourceEventEnvelopeAsync</c>.
    /// </summary>
    /// <param name="root">The root JSON element of the envelope.</param>
    /// <param name="jsonOptions">The JSON serializer options for deserializing nested payloads.</param>
    /// <returns>A fully-typed <see cref="ResourceEvent{TResource}"/>, or <c>null</c> if the data payload is absent.</returns>
    internal static ResourceEvent<TResource>? DeserializeResourceEventEnvelope(JsonElement root, JsonSerializerOptions jsonOptions)
    {
        // Extract the strongly-typed data payload
        TResource? data = default;
        if (root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind != JsonValueKind.Null)
        {
            data = dataElement.Deserialize<TResource>(jsonOptions);
        }
        else if (root.TryGetProperty("content", out var contentElement) && contentElement.ValueKind != JsonValueKind.Null)
        {
            data = contentElement.Deserialize<TResource>(jsonOptions);
        }

        if (data is null)
        {
            return null;
        }

        // Parse the change type from string → enum
        var changeType = ResourceEventChangeType.Updated;
        if (root.TryGetProperty("changeType", out var changeTypeElement) && changeTypeElement.ValueKind == JsonValueKind.String)
        {
            var changeTypeStr = changeTypeElement.GetString();
            if (changeTypeStr is not null)
            {
                _ = Enum.TryParse(changeTypeStr, ignoreCase: true, out changeType);
            }
        }

        // Extract the event id
        var eventId = string.Empty;
        if (root.TryGetProperty("eventId", out var eventIdElement) && eventIdElement.ValueKind == JsonValueKind.String)
        {
            eventId = eventIdElement.GetString() ?? string.Empty;
        }

        var resourceEvent = new ResourceEvent<TResource>(changeType, eventId, data);

        // Populate optional envelope fields
        if (root.TryGetProperty("href", out var hrefElement) && hrefElement.ValueKind == JsonValueKind.String)
        {
            resourceEvent.Href = hrefElement.GetString();
        }

        if (root.TryGetProperty("correlationId", out var corrElement) && corrElement.ValueKind == JsonValueKind.String)
        {
            resourceEvent.CorrelationId = corrElement.GetString();
        }

        // Each dictionary is read as SendAsync reads a resource event's (ResourceEventReader.ReadDictionary): a number as an
        // int, or a double when it is no int; a string as a string; a boolean as a bool; an object or array as a JsonElement;
        // a null value as null.
        if (root.TryGetProperty("changedValues", out var changedElement) && changedElement.ValueKind == JsonValueKind.Object)
        {
            resourceEvent.ChangedValues = ResourceEventReader.ReadDictionary(changedElement, jsonOptions);
        }

        if (root.TryGetProperty("metadata", out var metaElement) && metaElement.ValueKind == JsonValueKind.Object)
        {
            resourceEvent.Metadata = ResourceEventReader.ReadDictionary(metaElement, jsonOptions)!;
        }

        if (root.TryGetProperty("secureData", out var secElement) && secElement.ValueKind == JsonValueKind.Object)
        {
            resourceEvent.SecureData = ResourceEventReader.ReadDictionary(secElement, jsonOptions)!;
        }

        return resourceEvent;
    }

    /// <inheritdoc />
    internal override async ValueTask<ResourceEvent<TResource>?> ReadAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        if (context.Response.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        using var doc = await JsonDocument.ParseAsync(
            await context.Response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return DeserializeResourceEventEnvelope(doc.RootElement, context.JsonOptions);
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Blueprint.Http;
using Blueprint.Model;

namespace Blueprint.HttpClient;

/// <summary>
/// Reads a resource event the host wrote into the resource event type the caller asked for, as legacy Blueprint's
/// <c>JsonConverterFactoryForResourceEventOfT</c> did (Signature365 <c>8b40ecc961</c>,
/// <c>Blueprint.Http/JsonConverterFactoryForResourceEventOfT.cs</c>), which legacy's client read every response with
/// (<c>Common.Core/Api/BlueprintHttpClient.cs:40-44</c>, <c>:201-210</c>).
/// </summary>
/// <remarks>
/// <para>
/// System.Text.Json cannot read <see cref="ResourceCreated{TResource}"/>, <see cref="ResourceUpdated{TResource}"/> or
/// <see cref="ResourceDeleted{TResource}"/>: the <c>eventSubId</c> of their deserialisation constructor binds to no property.
/// Legacy never used a constructor. It made an uninitialised instance of the requested type (<c>:61</c>), gave it the
/// generic argument as its resource type (<c>:64</c>), and set each wire property by its exact name (<c>:127-169</c>),
/// ignoring any other. This does the same, for the same types: generic types assignable to <see cref="ResourceEvent"/>
/// (<c>:16-20</c>), <see cref="ResourceEvent{TResource}"/> included. The instance comes from the delegate the reader bound
/// when it was constructed (<see cref="Readers.ResourceEventResponseReader{TEvent, TResource}"/>): Blueprint's internal
/// constructor that sets nothing for its own event types, and an uninitialised instance for any other, so a member the wire
/// does not carry is left unset either way.
/// </para>
/// <para>
/// A dictionary's values are read as legacy read them (<c>:231-263</c>): a number as an <see cref="int"/>, a string as a
/// <see cref="string"/>, a boolean as a <see cref="bool"/>, and an object or array as a <see cref="JsonElement"/>. Two
/// inputs legacy threw for are read instead: a number that is not an <see cref="int"/>, as a <see cref="double"/>, and a
/// <see langword="null"/> value, as <see langword="null"/>. So is an absent or <see langword="null"/> <c>metadata</c> or
/// <c>secureData</c>, as an empty dictionary, which legacy left <see langword="null"/>: the host always writes both, and
/// the properties are never <see langword="null"/> otherwise.
/// </para>
/// </remarks>
internal static class ResourceEventReader
{
    /// <summary>
    /// Gets a value indicating whether <paramref name="type"/> is a resource event type this reads: a generic, concrete type
    /// assignable to <see cref="ResourceEvent"/> that derives from a <see cref="ResourceEvent{TResource}"/>.
    /// </summary>
    /// <param name="type">The type the caller asked for.</param>
    /// <returns><see langword="true"/> when this reads the type.</returns>
    internal static bool CanRead(Type type)
    {
        return type.IsGenericType && !type.IsAbstract && typeof(ResourceEvent).IsAssignableFrom(type) && FindResourceType(type) is not null;
    }

    /// <summary>
    /// Reads the response body as a <typeparamref name="TEvent"/>, failing for a JSON <c>null</c> as System.Text.Json fails
    /// for a read that ends early: how a union's member is read, where a null is no member to return.
    /// </summary>
    /// <typeparam name="TEvent">A type <see cref="CanRead"/> accepts.</typeparam>
    /// <typeparam name="TResource">The resource type of <typeparamref name="TEvent"/>.</typeparam>
    /// <param name="create">Makes the new event, with nothing set, that is read into.</param>
    /// <param name="content">The response content.</param>
    /// <param name="options">The options the event's data and dictionaries' nested values are read with.</param>
    /// <param name="dataTypeInfo">The metadata the event's data is read with, or <see langword="null"/> to read it with <paramref name="options"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The event.</returns>
    internal static async Task<TEvent> ReadAsync<TEvent, TResource>(
        Func<TEvent> create,
        HttpContent content,
        JsonSerializerOptions options,
        JsonTypeInfo<TResource>? dataTypeInfo,
        CancellationToken cancellationToken)
        where TEvent : ResourceEvent<TResource>
        where TResource : ApiResource
    {
        return await ReadOrNullAsync(create, content, options, dataTypeInfo, cancellationToken).ConfigureAwait(false)
            ?? throw new JsonException("Unexpected end when reading JSON.");
    }

    /// <summary>
    /// Reads the response body as a <typeparamref name="TEvent"/>, or as <see langword="null"/> when it is a JSON <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Legacy's converter did not handle null (<c>Blueprint.Http/JsonConverterFactoryForResourceEventOfT.cs:39</c>,
    /// <c>:53-58</c>), so System.Text.Json did not call it for a <c>null</c> token, and legacy's read of one was
    /// <see langword="null"/>.
    /// </remarks>
    /// <typeparam name="TEvent">A type <see cref="CanRead"/> accepts.</typeparam>
    /// <typeparam name="TResource">The resource type of <typeparamref name="TEvent"/>.</typeparam>
    /// <param name="create">Makes the new event, with nothing set, that is read into.</param>
    /// <param name="content">The response content.</param>
    /// <param name="options">The options the event's data and dictionaries' nested values are read with.</param>
    /// <param name="dataTypeInfo">The metadata the event's data is read with, or <see langword="null"/> to read it with <paramref name="options"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The event, or <see langword="null"/> for a JSON <c>null</c>.</returns>
    internal static async Task<TEvent?> ReadOrNullAsync<TEvent, TResource>(
        Func<TEvent> create,
        HttpContent content,
        JsonSerializerOptions options,
        JsonTypeInfo<TResource>? dataTypeInfo,
        CancellationToken cancellationToken)
        where TEvent : ResourceEvent<TResource>
        where TResource : ApiResource
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        return document.RootElement.ValueKind == JsonValueKind.Null ? null : Read(create, document.RootElement, options, dataTypeInfo);
    }

    /// <summary>
    /// Finds the resource type of a resource event type: the generic argument of the <see cref="ResourceEvent{TResource}"/>
    /// it is or derives from.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>The resource type, or <see langword="null"/> when <paramref name="type"/> is no such event.</returns>
    internal static Type? FindResourceType(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(ResourceEvent<>))
            {
                return current.GetGenericArguments()[0];
            }
        }

        return null;
    }

    /// <summary>
    /// Reads <paramref name="root"/> into the new event <paramref name="create"/> makes, which has nothing set.
    /// </summary>
    /// <exception cref="JsonException">When <paramref name="root"/> is not an object.</exception>
    private static TEvent Read<TEvent, TResource>(Func<TEvent> create, JsonElement root, JsonSerializerOptions options, JsonTypeInfo<TResource>? dataTypeInfo)
        where TEvent : ResourceEvent<TResource>
        where TResource : ApiResource
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Unexpected end when reading JSON.");
        }

        var made = create();

        // Set through the base type, as every member below is: the typed Data hides the base's.
        ResourceEvent resourceEvent = made;
        resourceEvent.ResourceType = typeof(TResource);

        foreach (var property in root.EnumerateObject())
        {
            var value = property.Value;

            switch (property.Name)
            {
                case "eventId":
                    resourceEvent.EventId = value.GetString()!;
                    break;
                case "changeType":
                    resourceEvent.ChangeType = Enum.Parse<ResourceEventChangeType>(value.GetString()!);
                    break;
                case "created":
                    resourceEvent.Created = value.GetDateTimeOffset();
                    break;
                case "resourceObject":
                    resourceEvent.ResourceObject = value.GetString()!;
                    break;
                case "href":
                    resourceEvent.Href = value.GetString();
                    break;
                case "data":
                    resourceEvent.Data = dataTypeInfo is null ? value.Deserialize<TResource>(options) : value.Deserialize(dataTypeInfo);
                    break;
                case "changedValues":
                    resourceEvent.ChangedValues = ReadDictionary(value, options);
                    break;
                case "metadata":
                    resourceEvent.Metadata = ReadDictionary(value, options)!;
                    break;
                case "secureData":
                    resourceEvent.SecureData = ReadDictionary(value, options)!;
                    break;
                case "correlationId":
                    resourceEvent.CorrelationId = value.GetString();
                    break;
            }
        }

        // Field initialisers never ran on an uninitialised instance, and a JSON null leaves either dictionary null.
        resourceEvent.Metadata ??= new Dictionary<string, object>();
        resourceEvent.SecureData ??= new Dictionary<string, object>();

        return made;
    }

    /// <summary>
    /// Reads a resource event's dictionary (<c>metadata</c>, <c>secureData</c> or <c>changedValues</c>) as legacy read one
    /// (see the class remarks); <c>SendResourceEventEnvelopeAsync</c> reads the envelope's with it too.
    /// </summary>
    /// <param name="value">The dictionary's JSON value.</param>
    /// <param name="options">The options that say whether an object or array value is a <see cref="JsonElement"/> or a <see cref="JsonNode"/>.</param>
    /// <returns>The dictionary, or <see langword="null"/> for a JSON <c>null</c>.</returns>
    /// <exception cref="JsonException">When <paramref name="value"/> is neither an object nor <c>null</c>.</exception>
    internal static Dictionary<string, object>? ReadDictionary(JsonElement value, JsonSerializerOptions options)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Expected an object for a resource event dictionary.");
        }

        var dictionary = new Dictionary<string, object>();

        // An object or array is what reading it as an object gives, without the options' metadata for object, which a
        // source-generated context does not have unless the user names it: a JsonElement, or a JsonNode when the options say so.
        foreach (var entry in value.EnumerateObject())
        {
            dictionary[entry.Name] = entry.Value.ValueKind switch
            {
                // Boxed apart: a conditional of int and double would make every number a double.
                JsonValueKind.Number => entry.Value.TryGetInt32(out var number) ? (object)number : entry.Value.GetDouble(),
                JsonValueKind.String => entry.Value.GetString()!,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Object or JsonValueKind.Array => options.UnknownTypeHandling == JsonUnknownTypeHandling.JsonNode
                    ? JsonNode.Parse(entry.Value.GetRawText(), new JsonNodeOptions { PropertyNameCaseInsensitive = options.PropertyNameCaseInsensitive })!
                    : entry.Value.Clone(),
                _ => null!,
            };
        }

        return dictionary;
    }
}

using System.Runtime.CompilerServices;
using System.Text.Json;
using Blueprint.Http;

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
/// (<c>:16-20</c>), <see cref="ResourceEvent{TResource}"/> included.
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
    /// Reads the response body as <paramref name="eventType"/>.
    /// </summary>
    /// <param name="eventType">A type <see cref="CanRead"/> accepts.</param>
    /// <param name="content">The response content.</param>
    /// <param name="options">The options the event's data and dictionaries' nested values are read with.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The event.</returns>
    internal static async Task<ResourceEvent> ReadAsync(
        Type eventType,
        HttpContent content,
        JsonSerializerOptions options,
        CancellationToken cancellationToken)
    {
        return await ReadOrNullAsync(eventType, content, options, cancellationToken).ConfigureAwait(false)
            ?? throw new JsonException("Unexpected end when reading JSON.");
    }

    /// <summary>
    /// Reads the response body as <paramref name="eventType"/>, or as <see langword="null"/> when it is a JSON <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Legacy's converter did not handle null (<c>Blueprint.Http/JsonConverterFactoryForResourceEventOfT.cs:39</c>,
    /// <c>:53-58</c>), so System.Text.Json did not call it for a <c>null</c> token, and legacy's read of one was
    /// <see langword="null"/>.
    /// </remarks>
    /// <param name="eventType">A type <see cref="CanRead"/> accepts.</param>
    /// <param name="content">The response content.</param>
    /// <param name="options">The options the event's data and dictionaries' nested values are read with.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The event, or <see langword="null"/> for a JSON <c>null</c>.</returns>
    internal static async Task<ResourceEvent?> ReadOrNullAsync(
        Type eventType,
        HttpContent content,
        JsonSerializerOptions options,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        return document.RootElement.ValueKind == JsonValueKind.Null ? null : Read(eventType, document.RootElement, options);
    }

    /// <summary>
    /// Reads <paramref name="root"/> as <paramref name="eventType"/>.
    /// </summary>
    /// <param name="eventType">A type <see cref="CanRead"/> accepts.</param>
    /// <param name="root">The event's JSON object.</param>
    /// <param name="options">The options the event's data and dictionaries' nested values are read with.</param>
    /// <returns>The event.</returns>
    /// <exception cref="JsonException">When <paramref name="root"/> is not an object.</exception>
    internal static ResourceEvent Read(Type eventType, JsonElement root, JsonSerializerOptions options)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Unexpected end when reading JSON.");
        }

        var resourceType = FindResourceType(eventType)
            ?? throw new ArgumentException($"{eventType} is not a resource event type with a resource type.", nameof(eventType));

        var resourceEvent = (ResourceEvent)RuntimeHelpers.GetUninitializedObject(eventType);
        resourceEvent.ResourceType = resourceType;

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
                    resourceEvent.Data = value.Deserialize(resourceType, options);
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

        // Field initialisers never ran on the uninitialised instance.
        resourceEvent.Metadata ??= new Dictionary<string, object>();
        resourceEvent.SecureData ??= new Dictionary<string, object>();

        return resourceEvent;
    }

    private static Type? FindResourceType(Type type)
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
    /// Reads a resource event's dictionary (<c>metadata</c>, <c>secureData</c> or <c>changedValues</c>) as legacy read one
    /// (see the class remarks); <c>SendResourceEventEnvelopeAsync</c> reads the envelope's with it too.
    /// </summary>
    /// <param name="value">The dictionary's JSON value.</param>
    /// <param name="options">The options an object or array value is read with.</param>
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

        foreach (var entry in value.EnumerateObject())
        {
            dictionary[entry.Name] = entry.Value.ValueKind switch
            {
                // Boxed apart: a conditional of int and double would make every number a double.
                JsonValueKind.Number => entry.Value.TryGetInt32(out var number) ? (object)number : entry.Value.GetDouble(),
                JsonValueKind.String => entry.Value.GetString()!,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Object or JsonValueKind.Array => entry.Value.Deserialize<object>(options)!,
                _ => null!,
            };
        }

        return dictionary;
    }
}

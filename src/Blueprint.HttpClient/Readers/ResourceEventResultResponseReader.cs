using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.HttpClient.Readers;

/// <summary>
/// Reads a response declared as a <see cref="ResourceEventResult{T}"/> from the envelope the host writes for one
/// (<c>HttpOperationResultExecutor.WriteResourceEventEnvelopeAsync</c>), which no constructor of it binds.
/// </summary>
/// <remarks>
/// <para>
/// The result is its <c>changeType</c>, <c>eventId</c>, <c>data</c> as the content, the <c>metadata</c>,
/// <c>secureData</c> and <c>changedValues</c> the host writes when set, each read as a resource event's are, and the
/// <c>correlationId</c>. The envelope carries no resource id, so the result's is empty.
/// </para>
/// <para>
/// A 204, which is how the host writes a deleted one, carries no content to read and is legacy's "could not understand"
/// failure. In a union it is a 201 or 200 JSON body, told apart from an event of another resource by the
/// <c>resourceObject</c> the host writes.
/// </para>
/// </remarks>
/// <typeparam name="T">The type of the result's content.</typeparam>
public sealed class ResourceEventResultResponseReader<T> : ResponseReader<ResourceEventResult<T>>
    where T : notnull
{
    private readonly string _resourceObject = ApiResource.GetTypeName(typeof(T));
    private readonly JsonTypeInfo<T>? _dataTypeInfo;

    /// <summary>
    /// Initialises a new instance of the <see cref="ResourceEventResultResponseReader{T}"/> class.
    /// </summary>
    /// <param name="dataTypeInfo">
    /// The metadata to read the content with, from the transport's options; or <see langword="null"/> to read it with the
    /// transport's options' own.
    /// </param>
    public ResourceEventResultResponseReader(JsonTypeInfo<T>? dataTypeInfo = null)
    {
        this._dataTypeInfo = dataTypeInfo;
    }

    /// <inheritdoc />
    internal override string? EventResourceObject => this._resourceObject;

    /// <inheritdoc />
    internal override async ValueTask<ResourceEventResult<T>> ReadAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        if (context.Status == (int)HttpStatusCode.NoContent)
        {
            throw context.CreateNotUnderstoodException();
        }

        return await this.ReadEnvelopeAsync(context, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    internal override bool IsJsonBodyOfStatus(int status)
    {
        // A created one is a 201 and an updated one a 200; a deleted one is an empty 204, which carries no content to read.
        return status is (int)HttpStatusCode.OK or (int)HttpStatusCode.Created;
    }

    /// <inheritdoc />
    internal override async ValueTask<(bool Read, ResourceEventResult<T> Value)> ReadJsonMemberAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        return (true, await this.ReadEnvelopeAsync(context, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Reads the body as the envelope the host writes for a <see cref="ResourceEventResult{T}"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">When the body is not an envelope with a change type and data.</exception>
    private async Task<ResourceEventResult<T>> ReadEnvelopeAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await context.Response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("changeType", out var changeTypeElement)
            || changeTypeElement.ValueKind != JsonValueKind.String
            || !Enum.TryParse<ChangeType>(changeTypeElement.GetString(), out var changeType)
            || !root.TryGetProperty("data", out var dataElement)
            || dataElement.ValueKind == JsonValueKind.Null)
        {
            throw new InvalidOperationException($"The response is not the resource event envelope a {typeof(ResourceEventResult<T>)} is written as.");
        }

        var eventId = root.TryGetProperty("eventId", out var eventIdElement) && eventIdElement.ValueKind == JsonValueKind.String
            ? eventIdElement.GetString()!
            : string.Empty;
        var data = (this._dataTypeInfo is null ? dataElement.Deserialize<T>(context.JsonOptions) : dataElement.Deserialize(this._dataTypeInfo))!;
        var result = new ResourceEventResult<T>(changeType, eventId, data);

        foreach (var (key, value) in ReadEnvelopeDictionary(root, "metadata", context.JsonOptions))
        {
            result.WithMetadata(key, value);
        }

        foreach (var (key, value) in ReadEnvelopeDictionary(root, "secureData", context.JsonOptions))
        {
            result.WithSecureData(key, value);
        }

        foreach (var (key, value) in ReadEnvelopeDictionary(root, "changedValues", context.JsonOptions))
        {
            result.WithChangedValue(key, value);
        }

        if (root.TryGetProperty("correlationId", out var correlationIdElement) && correlationIdElement.ValueKind == JsonValueKind.String)
        {
            result.CorrelationId = correlationIdElement.GetString();
        }

        return result;
    }

    private static Dictionary<string, object> ReadEnvelopeDictionary(JsonElement root, string propertyName, JsonSerializerOptions options)
    {
        return root.TryGetProperty(propertyName, out var element) && ResourceEventReader.ReadDictionary(element, options) is { } dictionary
            ? dictionary
            : [];
    }
}

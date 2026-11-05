using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using Blueprint.Http;
using Blueprint.Model;

namespace Blueprint.HttpClient.Readers;

/// <summary>
/// Reads a response as JSON of <typeparamref name="T"/>: the reader for any declared result no other reader is for
/// (see <see cref="ResponseReader{T}"/>), <see cref="string"/> included.
/// </summary>
/// <remarks>
/// <para>
/// A JSON <c>null</c> for a <typeparamref name="T"/> that can hold one is legacy's <c>response_empty</c> failure, not a
/// <see langword="null"/> result (Signature365 <c>8b40ecc961</c>, <c>Common.Core/Api/BlueprintHttpClient.cs:199-208</c>);
/// a struct such as a <see cref="System.Text.Json.JsonElement"/> is returned as read. A <see cref="string"/> is a
/// <c>text/*</c> body as it is, and JSON otherwise. A 204 is legacy's "could not understand" failure, unless
/// <typeparamref name="T"/> is a result the host writes with no body (<see cref="Blueprint.Runtime.Http.HttpNoContentResult"/>,
/// <see cref="Blueprint.Runtime.Exec.NoResultOperationResult"/>, <see cref="Blueprint.Runtime.Http.HttpAcceptedResult"/> or
/// <see cref="Blueprint.Runtime.Exec.KeyOnlyResourceEventResult"/>), which a response with no media type is read as.
/// </para>
/// <para>
/// It is not for a result that has a reader of its own: <see cref="Blueprint.Runtime.Exec.OkResult{T}"/> and its siblings in
/// a union are read from their content only by <see cref="ContentResultResponseReader{TResult, TContent}"/>, and a resource
/// event or <see cref="Blueprint.Runtime.Exec.ResourceEventResult{T}"/> is no JSON System.Text.Json can bind.
/// </para>
/// </remarks>
/// <typeparam name="T">The declared result type.</typeparam>
public sealed class JsonResponseReader<T> : ResponseReader<T>
{
    private readonly JsonTypeInfo<T>? _typeInfo;
    private readonly bool _readsText;
    private readonly Func<int, object?>? _createBodiless;
    private readonly bool _isJsonMember;
    private readonly string? _eventResourceObject;

    /// <summary>
    /// Initialises a new instance of the <see cref="JsonResponseReader{T}"/> class.
    /// </summary>
    /// <param name="typeInfo">
    /// The metadata to read <typeparamref name="T"/> with, from the transport's options; or <see langword="null"/> to read it
    /// with the transport's options' own.
    /// </param>
    public JsonResponseReader(JsonTypeInfo<T>? typeInfo = null)
    {
        this._typeInfo = typeInfo;
        this._readsText = typeof(T) == typeof(string);
        this._createBodiless = BodilessResults.ForSemanticResult(typeof(T));

        // As a union's member, the host writes a plain value (sent as an OkResult) or a resource event as a 200 JSON body;
        // no other operation result read by this reader is a body.
        this._isJsonMember = typeof(ResourceEvent).IsAssignableFrom(typeof(T)) || !typeof(OperationResult).IsAssignableFrom(typeof(T));
        this._eventResourceObject = ResourceEventReader.FindResourceType(typeof(T)) is { } resourceType ? ApiResource.GetTypeName(resourceType) : null;
    }

    /// <inheritdoc />
    internal override string? EventResourceObject => this._eventResourceObject;

    /// <inheritdoc />
    internal override async ValueTask<T> ReadAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        var status = context.Status;

        if (status == (int)HttpStatusCode.NoContent)
        {
            return this._createBodiless?.Invoke(status) is T noContent ? noContent : throw context.CreateNotUnderstoodException();
        }

        var mediaType = context.MediaType;

        if (this._readsText && mediaType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true)
        {
            return (T)(object)await context.Response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        if (mediaType is null && this._createBodiless?.Invoke(status) is T bodiless)
        {
            return bodiless;
        }

        var value = await this.ReadJsonAsync(context, cancellationToken).ConfigureAwait(false);

        return value is null ? throw await context.CreateResponseEmptyExceptionAsync(cancellationToken).ConfigureAwait(false) : value;
    }

    /// <inheritdoc />
    internal override bool IsJsonBodyOfStatus(int status)
    {
        return this._isJsonMember && status == (int)HttpStatusCode.OK;
    }

    /// <inheritdoc />
    internal override async ValueTask<(bool Read, T Value)> ReadJsonMemberAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        var value = await this.ReadJsonAsync(context, cancellationToken).ConfigureAwait(false);

        return value is null ? default : (true, value);
    }

    /// <inheritdoc />
    internal override ValueTask<(bool Read, T Value)> ReadOtherMemberAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        return context.MediaType is null && this._createBodiless?.Invoke(context.Status) is T bodiless
            ? new ValueTask<(bool Read, T Value)>((true, bodiless))
            : default;
    }

    private async Task<T?> ReadJsonAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        return this._typeInfo is null
            ? await context.Response.Content.ReadFromJsonAsync<T>(context.JsonOptions, cancellationToken).ConfigureAwait(false)
            : await context.Response.Content.ReadFromJsonAsync(this._typeInfo, cancellationToken).ConfigureAwait(false);
    }
}

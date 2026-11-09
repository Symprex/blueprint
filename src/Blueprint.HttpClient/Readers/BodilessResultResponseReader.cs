using System.Net;
using System.Net.Http.Json;
using Blueprint.Http;
using Blueprint.Runtime.Exec;

namespace Blueprint.HttpClient.Readers;

/// <summary>
/// Reads a response declared as the base <see cref="StatusCodeResult"/>, <see cref="HttpResult"/>,
/// <see cref="OperationResult"/> or <see cref="object"/>, any of which a response with no body can be.
/// </summary>
/// <remarks>
/// <para>
/// A response with no media type, a 204 among them, is the nested <see cref="StatusCodeResult"/> singleton of its status,
/// or for <see cref="HttpResult"/>, a status-only <see cref="HttpResult"/>, as legacy's client made a
/// <c>StatusCodeResult</c> of whatever nothing else claimed (Signature365 <c>8b40ecc961</c>,
/// <c>Common.Core/Api/BlueprintHttpClient.cs:199-225</c>). Live: Microsoft365.Gateway's
/// <c>HandleGraphWebhookNotificationsCommand : ICommand&lt;HttpResult&gt;</c> forwarding Microsoft365's bodiless 202.
/// </para>
/// <para>
/// Any body with a media type is a <see cref="PlainTextResult"/> for each of them but <see cref="StatusCodeResult"/>, which
/// can hold one; a <see cref="StatusCodeResult"/> body is read as JSON, which the abstract type cannot be.
/// </para>
/// </remarks>
/// <typeparam name="T"><see cref="StatusCodeResult"/>, <see cref="HttpResult"/>, <see cref="OperationResult"/> or <see cref="object"/>.</typeparam>
public sealed class BodilessResultResponseReader<T> : ResponseReader<T>
{
    private readonly bool _holdsPlainText;
    private readonly bool _isHttpResult;
    private readonly bool _isStatusCodeResult;
    private readonly bool _isObject;

    /// <summary>
    /// Initialises a new instance of the <see cref="BodilessResultResponseReader{T}"/> class.
    /// </summary>
    /// <exception cref="ArgumentException"><typeparamref name="T"/> is none of the four types this reads.</exception>
    public BodilessResultResponseReader()
    {
        this._isStatusCodeResult = typeof(T) == typeof(StatusCodeResult);
        this._isHttpResult = typeof(T) == typeof(HttpResult);
        this._isObject = typeof(T) == typeof(object);

        if (!this._isStatusCodeResult && !this._isHttpResult && !this._isObject && typeof(T) != typeof(OperationResult))
        {
            throw new ArgumentException(
                $"A bodiless result reader reads a {typeof(StatusCodeResult)}, {typeof(HttpResult)}, {typeof(OperationResult)} or {typeof(object)}, not a {typeof(T)}.");
        }

        this._holdsPlainText = !this._isStatusCodeResult;
    }

    /// <inheritdoc />
    internal override async ValueTask<T> ReadAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        var status = context.Status;

        if (status == (int)HttpStatusCode.NoContent)
        {
            return this.CreateNoMediaTypeResult(status) is T noContent ? noContent : throw context.CreateNotUnderstoodException();
        }

        var mediaType = context.MediaType;

        if (this._holdsPlainText && !string.IsNullOrEmpty(mediaType))
        {
            return (T)(object)await context.ReadPlainTextAsync(mediaType, cancellationToken).ConfigureAwait(false);
        }

        if (mediaType is null && this.CreateNoMediaTypeResult(status) is T bodiless)
        {
            return bodiless;
        }

        var value = await context.Response.Content.ReadFromJsonAsync<T>(context.JsonOptions, cancellationToken).ConfigureAwait(false);

        return value is null ? throw await context.CreateResponseEmptyExceptionAsync(cancellationToken).ConfigureAwait(false) : value;
    }

    /// <inheritdoc />
    internal override bool IsJsonBodyOfStatus(int status)
    {
        // Of the four, only object is not an operation result, so only it is a plain value the host writes as a 200 body.
        return this._isObject && status == (int)HttpStatusCode.OK;
    }

    /// <inheritdoc />
    internal override async ValueTask<(bool Read, T Value)> ReadJsonMemberAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        var value = await context.Response.Content.ReadFromJsonAsync<T>(context.JsonOptions, cancellationToken).ConfigureAwait(false);

        return value is null ? default : (true, value);
    }

    /// <inheritdoc />
    internal override ValueTask<(bool Read, T Value)> ReadOtherMemberAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        // A union's base StatusCodeResult member is the singleton of a bodiless response's status; the other three hold no
        // member the host writes without a body.
        return this._isStatusCodeResult && context.MediaType is null && BodilessResults.FindStatusCodeResult(context.Status) is T bodiless
            ? new ValueTask<(bool Read, T Value)>((true, bodiless))
            : default;
    }

    /// <summary>
    /// The result a response with no media type is: a status-only <see cref="HttpResult"/> for <see cref="HttpResult"/>,
    /// and for the other three (each of which a <see cref="StatusCodeResult"/> is), the nested singleton of the status.
    /// </summary>
    private object? CreateNoMediaTypeResult(int status)
    {
        return this._isHttpResult
            ? new BodilessResults.BodilessHttpResult((HttpStatusCode)status)
            : BodilessResults.FindStatusCodeResult(status);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Blueprint.Runtime.Exec;

namespace Blueprint.HttpClient;

/// <summary>
/// Reads a success response as <typeparamref name="TResult"/>. Each reader handles one result shape, so a call whose
/// shape is known when it is compiled reads its response with no type tests; <see cref="BlueprintHttpClient{TApp}"/>
/// chooses among the same readers from the result type it is asked for.
/// </summary>
/// <remarks>
/// Readers are made by <see cref="BlueprintResponseReaders"/>; only Blueprint defines them.
/// </remarks>
/// <typeparam name="TResult">The result type.</typeparam>
public abstract class BlueprintResponseReader<TResult>
{
    /// <summary>
    /// Initialises a new instance of the <see cref="BlueprintResponseReader{TResult}"/> class.
    /// </summary>
    private protected BlueprintResponseReader()
    {
    }

    /// <summary>
    /// Reads the response, which the transport has already checked is a success.
    /// </summary>
    /// <param name="context">The request, the response and the options to read it with.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result.</returns>
    internal abstract Task<TResult> ReadAsync(BlueprintResponseContext context, CancellationToken cancellationToken);
}

/// <summary>
/// What a <see cref="BlueprintResponseReader{TResult}"/> reads: the request sent, its success response, the options the
/// body is read with and the operation's name, which a failure to understand the response names.
/// </summary>
/// <param name="Request">The request sent.</param>
/// <param name="Response">The success response.</param>
/// <param name="JsonOptions">The options the body is read with.</param>
/// <param name="OperationName">The operation's type name.</param>
internal readonly record struct BlueprintResponseContext(
    HttpRequestMessage Request,
    HttpResponseMessage Response,
    JsonSerializerOptions JsonOptions,
    string OperationName)
{
    /// <summary>
    /// The requested resource as a failure may name it: its path and query, never the host.
    /// </summary>
    /// <param name="request">The request, whose URI the client has made absolute by now.</param>
    /// <returns>The description.</returns>
    internal static string DescribeRequestedResource(HttpRequestMessage request)
    {
        return request.RequestUri switch
        {
            { IsAbsoluteUri: true } uri => uri.PathAndQuery,
            { } uri => uri.OriginalString,
            null => "the requested resource",
        };
    }

    /// <summary>
    /// Creates the exception legacy's client threw when a success response's JSON read was <see langword="null"/>: its
    /// <c>response_empty</c> problem, "Response deserialisation failed", "The response could not be deserialised", 502
    /// (Signature365 <c>8b40ecc961</c>, <c>Common.Core/Api/BlueprintHttpClient.cs:21-24</c>, <c>:203-208</c>). It carries
    /// the response's own status and body as every failure here does.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The exception to throw.</returns>
    internal async Task<BlueprintApiException> CreateResponseEmptyExceptionAsync(CancellationToken cancellationToken)
    {
        return new BlueprintApiException(
            new BlueprintApiException.ApiProblem(
                "Response deserialisation failed",
                "response_empty",
                "The response could not be deserialised",
                (int)HttpStatusCode.BadGateway),
            this.Response.StatusCode,
            await this.Response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false),
            innerException: null);
    }

    /// <summary>
    /// Creates the exception legacy's client threw for a response it could not understand (Signature365
    /// <c>8b40ecc961</c>, <c>Common.Core/Api/BlueprintHttpClient.cs:228</c>).
    /// </summary>
    /// <returns>The exception to throw.</returns>
    internal InvalidOperationException CreateNotUnderstoodException()
    {
        return new InvalidOperationException(
            $"Could not understand the response of calling {this.OperationName}: {DescribeRequestedResource(this.Request)}");
    }

    /// <summary>
    /// Reads the body as JSON, a <see langword="null"/> read (a JSON null, for a type that can hold one) being legacy's
    /// <c>response_empty</c> failure, not a null result.
    /// </summary>
    /// <typeparam name="T">The type to read.</typeparam>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The value read.</returns>
    internal async Task<T> ReadJsonOrThrowAsync<T>(CancellationToken cancellationToken)
    {
        var value = await this.Response.Content.ReadFromJsonAsync<T>(this.JsonOptions, cancellationToken).ConfigureAwait(false);

        return value is null
            ? throw await this.CreateResponseEmptyExceptionAsync(cancellationToken).ConfigureAwait(false)
            : value;
    }
}

/// <summary>
/// Reads a result that is not a union or a nested <see cref="StatusCodeResult"/>: an empty 204 as the result the host writes
/// that way, or a failure; a body with a media type as a <see cref="PlainTextResult"/> when the result can hold one; a
/// <c>text/*</c> body as the text when the result is a <see cref="string"/>; a response with no media type as the
/// bodiless result the host writes that way, when there is one; and otherwise the body, by the reader's body read.
/// </summary>
/// <typeparam name="T">The result type.</typeparam>
/// <remarks>
/// That is the order legacy's client tried them in (Signature365 <c>8b40ecc961</c>,
/// <c>Common.Core/Api/BlueprintHttpClient.cs:199-228</c>). Which steps apply is fixed when the reader is made.
/// </remarks>
internal sealed class ValueResponseReader<T> : BlueprintResponseReader<T>
{
    private readonly Func<int, T?>? _noMediaTypeResult;
    private readonly bool _acceptsPlainText;
    private readonly bool _isString;
    private readonly Func<BlueprintResponseContext, CancellationToken, Task<T>> _readBody;

    /// <summary>
    /// Initialises a new instance of the <see cref="ValueResponseReader{T}"/> class.
    /// </summary>
    /// <param name="noMediaTypeResult">The result a response with no media type and the given status is, or <see langword="null"/> when the result type has no bodiless form.</param>
    /// <param name="acceptsPlainText">Whether the result type can hold a <see cref="PlainTextResult"/>.</param>
    /// <param name="isString">Whether the result type is <see cref="string"/>.</param>
    /// <param name="readBody">Reads the body when no earlier step claimed the response.</param>
    internal ValueResponseReader(
        Func<int, T?>? noMediaTypeResult,
        bool acceptsPlainText,
        bool isString,
        Func<BlueprintResponseContext, CancellationToken, Task<T>> readBody)
    {
        this._noMediaTypeResult = noMediaTypeResult;
        this._acceptsPlainText = acceptsPlainText;
        this._isString = isString;
        this._readBody = readBody;
    }

    /// <inheritdoc />
    internal override async Task<T> ReadAsync(BlueprintResponseContext context, CancellationToken cancellationToken)
    {
        var response = context.Response;

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            // A result the host writes as an empty 204 is read as that result, as a union's member is. Otherwise, as legacy's
            // client had nothing to read in a 204, any shape legacy's StatusCodeResult branch covered gets a
            // StatusCodeResult-shaped result, and anything else throws.
            return this._noMediaTypeResult is not null && this._noMediaTypeResult((int)HttpStatusCode.NoContent) is { } bodiless
                ? bodiless
                : throw context.CreateNotUnderstoodException();
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;

        if (this._acceptsPlainText && !string.IsNullOrEmpty(contentType))
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return (T)(object)new PlainTextResult(text, response.StatusCode) { ContentType = contentType };
        }

        if (this._isString && contentType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true)
        {
            return (T)(object)await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        // No media type at all, any status: legacy's last resort for whatever nothing else claimed. Live: Microsoft365.Gateway's
        // HandleGraphWebhookNotificationsCommand : ICommand<HttpResult> forwarding Microsoft365's bodiless 202.
        if (contentType is null && this._noMediaTypeResult is not null && this._noMediaTypeResult((int)response.StatusCode) is { } noMediaTypeResult)
        {
            return noMediaTypeResult;
        }

        return await this._readBody(context, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Reads a nested <see cref="StatusCodeResult"/> (for example <see cref="StatusCodeResult.Accepted"/>), which the host writes
/// with no body: its singleton when the response's status is its own, and <see langword="null"/> otherwise, for the caller
/// to decide whether that is an error. This is the pattern S365 gateway code uses via
/// <c>CallAsync&lt;StatusCodeResult.Accepted&gt;(...)</c>.
/// </summary>
/// <typeparam name="T">The nested status result type.</typeparam>
internal sealed class StatusCodeResultResponseReader<T> : BlueprintResponseReader<T>
    where T : StatusCodeResult
{
    private readonly T? _instance;

    /// <summary>
    /// Initialises a new instance of the <see cref="StatusCodeResultResponseReader{T}"/> class.
    /// </summary>
    /// <param name="instance">The type's singleton, or <see langword="null"/> for a type with none, which then never matches.</param>
    internal StatusCodeResultResponseReader(T? instance)
    {
        this._instance = instance;
    }

    /// <inheritdoc />
    internal override Task<T> ReadAsync(BlueprintResponseContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(
            this._instance is not null && this._instance.StatusCode == (int)context.Response.StatusCode ? this._instance : default!);
    }
}

/// <summary>
/// Reads nothing: the transport has already checked the response is a success, which is all a call with no result needs.
/// </summary>
internal sealed class NoResultResponseReader : BlueprintResponseReader<bool>
{
    /// <summary>
    /// The reader.
    /// </summary>
    internal static readonly NoResultResponseReader Instance = new();

    /// <inheritdoc />
    internal override Task<bool> ReadAsync(BlueprintResponseContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(true);
    }
}

/// <summary>
/// Reads a response with a read of its own, for the results only <see cref="BlueprintHttpClient{TApp}"/>'s envelope-reading
/// methods ask for.
/// </summary>
/// <typeparam name="TResult">The result type.</typeparam>
internal sealed class DelegateResponseReader<TResult> : BlueprintResponseReader<TResult>
{
    private readonly Func<BlueprintResponseContext, CancellationToken, Task<TResult>> _read;

    /// <summary>
    /// Initialises a new instance of the <see cref="DelegateResponseReader{TResult}"/> class.
    /// </summary>
    /// <param name="read">The read.</param>
    internal DelegateResponseReader(Func<BlueprintResponseContext, CancellationToken, Task<TResult>> read)
    {
        this._read = read;
    }

    /// <inheritdoc />
    internal override Task<TResult> ReadAsync(BlueprintResponseContext context, CancellationToken cancellationToken)
    {
        return this._read(context, cancellationToken);
    }
}

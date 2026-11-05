using System.Net;
using System.Text.Json;
using Blueprint.Runtime.Exec;

namespace Blueprint.HttpClient.Readers;

/// <summary>
/// What a <see cref="ResponseReader{T}"/> reads a success response with: the response, the request it answers (whose URI
/// names the resource in a failure's message), the operation's name and the transport's JSON options.
/// </summary>
internal readonly struct ResponseReadContext
{
    /// <summary>
    /// Initialises a new instance of the <see cref="ResponseReadContext"/> struct.
    /// </summary>
    /// <param name="request">The request the response answers.</param>
    /// <param name="response">The success response.</param>
    /// <param name="operationName">The operation's name, for a failure's message.</param>
    /// <param name="jsonOptions">The transport's JSON options.</param>
    internal ResponseReadContext(HttpRequestMessage request, HttpResponseMessage response, string operationName, JsonSerializerOptions jsonOptions)
    {
        this.Request = request;
        this.Response = response;
        this.OperationName = operationName;
        this.JsonOptions = jsonOptions;
    }

    /// <summary>
    /// Gets the request the response answers, whose URI the client has made absolute by now.
    /// </summary>
    internal HttpRequestMessage Request { get; }

    /// <summary>
    /// Gets the success response.
    /// </summary>
    internal HttpResponseMessage Response { get; }

    /// <summary>
    /// Gets the operation's name, which a failure's message names.
    /// </summary>
    internal string OperationName { get; }

    /// <summary>
    /// Gets the transport's JSON options, which a reader not given a type info reads with.
    /// </summary>
    internal JsonSerializerOptions JsonOptions { get; }

    /// <summary>
    /// Gets the response's status.
    /// </summary>
    internal int Status => (int)this.Response.StatusCode;

    /// <summary>
    /// Gets the response's media type, or <see langword="null"/> when it has none (a bodiless response).
    /// </summary>
    internal string? MediaType => this.Response.Content.Headers.ContentType?.MediaType;

    /// <summary>
    /// Creates the exception legacy's client threw for a response it could not read as the declared result
    /// (Signature365 <c>8b40ecc961</c>, <c>Common.Core/Api/BlueprintHttpClient.cs:228</c>). It names the path called,
    /// never the host.
    /// </summary>
    /// <returns>The exception to throw.</returns>
    internal InvalidOperationException CreateNotUnderstoodException()
    {
        return new InvalidOperationException(
            $"Could not understand the response of calling {this.OperationName}: {BlueprintHttpClient.DescribeRequestedResource(this.Request)}");
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
    /// Reads the body as the <see cref="PlainTextResult"/> the host wrote: its text, with the response's status and media type.
    /// </summary>
    /// <param name="mediaType">The response's media type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result.</returns>
    internal async Task<PlainTextResult> ReadPlainTextAsync(string mediaType, CancellationToken cancellationToken)
    {
        var text = await this.Response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return new PlainTextResult(text, this.Response.StatusCode)
        {
            ContentType = mediaType,
        };
    }
}

using System.Net;
using System.Net.Http.Json;
using Blueprint.Runtime.Exec;

namespace Blueprint.HttpClient.Readers;

/// <summary>
/// Reads a response declared as a <see cref="PlainTextResult"/>: a body with any media type is its text, with the
/// response's status and media type, as the host writes one.
/// </summary>
/// <remarks>
/// A 204 is legacy's "could not understand" failure, and a body with no media type is read as JSON, as the client read
/// both before its readers existed. In a union, only a body with a media type is this member.
/// </remarks>
public sealed class PlainTextResponseReader : ResponseReader<PlainTextResult>
{
    /// <inheritdoc />
    internal override async ValueTask<PlainTextResult> ReadAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        if (context.Status == (int)HttpStatusCode.NoContent)
        {
            throw context.CreateNotUnderstoodException();
        }

        var mediaType = context.MediaType;

        if (!string.IsNullOrEmpty(mediaType))
        {
            return await context.ReadPlainTextAsync(mediaType, cancellationToken).ConfigureAwait(false);
        }

        var value = await context.Response.Content.ReadFromJsonAsync<PlainTextResult>(context.JsonOptions, cancellationToken).ConfigureAwait(false);

        return value ?? throw await context.CreateResponseEmptyExceptionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    internal override async ValueTask<(bool Read, PlainTextResult Value)> ReadOtherMemberAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        var mediaType = context.MediaType;

        return mediaType is null ? default : (true, await context.ReadPlainTextAsync(mediaType, cancellationToken).ConfigureAwait(false));
    }
}

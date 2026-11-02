using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Blueprint.Model;

namespace Blueprint.HttpClient.Readers;

/// <summary>
/// Reads a response declared as a <see cref="Results{T1, T2}"/> as the member the host wrote, with the reader of each
/// member, rather than as the union.
/// </summary>
/// <remarks>
/// <para>
/// With a JSON body, the one member the host writes as a JSON body of that status claims it: a plain type or a resource
/// event at a 200, an <see cref="Blueprint.Runtime.Exec.OkResult{T}"/>, <see cref="Blueprint.Runtime.Exec.CreatedResult{T}"/>
/// or <see cref="Blueprint.Runtime.Exec.AcceptedResult{T}"/> at its own status, read as its content, and a
/// <see cref="Blueprint.Runtime.Exec.ResourceEventResult{T}"/> at a 201 or 200, read from its envelope. When both could be
/// that body, it cannot say which the host wrote, so rather than read it as whichever is declared first this throws, naming
/// them; the exception is two events of distinct resources, which the body's <c>resourceObject</c> tells apart.
/// </para>
/// <para>
/// With any other body, the first member claims it that the host writes without a body at the response's status (a nested
/// <see cref="Blueprint.Runtime.Exec.StatusCodeResult"/> by its own status, the base type by the nested result of the
/// status, and the other results the host writes with no body), or, for a body with a media type, a
/// <see cref="Blueprint.Runtime.Exec.PlainTextResult"/> member.
/// </para>
/// <para>
/// A response no member claims, a 204 included, is a contract mismatch: a default union would hold no member at all. Legacy's
/// client threw for any response it could not understand (Signature365 <c>8b40ecc961</c>,
/// <c>Common.Core/Api/BlueprintHttpClient.cs:228</c>).
/// </para>
/// </remarks>
/// <typeparam name="T1">The first member type.</typeparam>
/// <typeparam name="T2">The second member type.</typeparam>
public sealed class UnionResponseReader<T1, T2> : ResponseReader<Results<T1, T2>>
    where T1 : notnull
    where T2 : notnull
{
    private readonly ResponseReader<T1> _first;
    private readonly ResponseReader<T2> _second;

    /// <summary>
    /// Initialises a new instance of the <see cref="UnionResponseReader{T1, T2}"/> class.
    /// </summary>
    /// <param name="first">The reader of <typeparamref name="T1"/>, chosen as for a result of that type alone.</param>
    /// <param name="second">The reader of <typeparamref name="T2"/>, chosen as for a result of that type alone.</param>
    public UnionResponseReader(ResponseReader<T1> first, ResponseReader<T2> second)
    {
        this._first = first ?? throw new ArgumentNullException(nameof(first));
        this._second = second ?? throw new ArgumentNullException(nameof(second));
    }

    /// <inheritdoc />
    internal override async ValueTask<Results<T1, T2>> ReadAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        var union = await this.ReadMemberAsync(context, cancellationToken).ConfigureAwait(false);

        return union ?? throw new InvalidOperationException(
            $"The response ({context.Status}, {context.MediaType ?? "no content"}) matches none of the results {typeof(Results<T1, T2>)} declares.");
    }

    /// <inheritdoc />
    internal override bool IsJsonBodyOfStatus(int status)
    {
        // A union nested in a union is no operation result, so it is read as a plain value would be.
        return status == (int)HttpStatusCode.OK;
    }

    /// <inheritdoc />
    internal override async ValueTask<(bool Read, Results<T1, T2> Value)> ReadJsonMemberAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        return (true, await context.Response.Content.ReadFromJsonAsync<Results<T1, T2>>(context.JsonOptions, cancellationToken).ConfigureAwait(false));
    }

    private async Task<Results<T1, T2>?> ReadMemberAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        var mediaType = context.MediaType;
        var isJson = mediaType is not null
                     && (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                         || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));

        if (!isJson)
        {
            var (firstRead, first) = await this._first.ReadOtherMemberAsync(context, cancellationToken).ConfigureAwait(false);

            if (firstRead)
            {
                return first;
            }

            var (secondRead, second) = await this._second.ReadOtherMemberAsync(context, cancellationToken).ConfigureAwait(false);

            return secondRead ? second : null;
        }

        var status = context.Status;
        var isFirst = this._first.IsJsonBodyOfStatus(status);
        var isSecond = this._second.IsJsonBodyOfStatus(status);

        if (isFirst && isSecond && this._first.EventResourceObject is { } firstResource && this._second.EventResourceObject is { } secondResource)
        {
            (isFirst, isSecond) = await SelectByResourceObjectAsync(context, firstResource, secondResource, cancellationToken).ConfigureAwait(false);
        }

        if (isFirst && isSecond)
        {
            throw new InvalidOperationException(
                $"The response ({status}, {mediaType}) could be more than one of the results {typeof(Results<T1, T2>)} declares " +
                $"({typeof(T1)}, {typeof(T2)}), so which the host wrote cannot be told.");
        }

        if (isFirst)
        {
            var (read, value) = await this._first.ReadJsonMemberAsync(context, cancellationToken).ConfigureAwait(false);

            return read ? value : null;
        }

        if (isSecond)
        {
            var (read, value) = await this._second.ReadJsonMemberAsync(context, cancellationToken).ConfigureAwait(false);

            return read ? value : null;
        }

        return null;
    }

    /// <summary>
    /// Narrows two events that could each be the body to the one of the resource the body names. The host writes an event's
    /// <c>resourceObject</c> as its resource's type name (<see cref="ApiResource.GetTypeName"/>), fixed by the event's type,
    /// where a legacy event's <c>eventId</c> carries a sub-id its handler chose at run time and a
    /// <see cref="Blueprint.Runtime.Exec.ResourceEventResult{T}"/>'s is its handler's too. So two events of distinct
    /// resources, which the generator accepts in one union, are told apart by it, as the OpenAPI document's discriminator
    /// says for legacy events.
    /// </summary>
    /// <returns>Which of the two the body could still be: both when it names neither, or names both.</returns>
    private static async Task<(bool First, bool Second)> SelectByResourceObjectAsync(
        ResponseReadContext context,
        string firstResource,
        string secondResource,
        CancellationToken cancellationToken)
    {
        // Read as bytes, which buffers the content without consuming the stream the member is read from afterwards.
        using var document = JsonDocument.Parse(await context.Response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));

        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("resourceObject", out var resourceObject)
            || resourceObject.ValueKind != JsonValueKind.String)
        {
            return (true, true);
        }

        var name = resourceObject.GetString();
        var isFirst = string.Equals(firstResource, name, StringComparison.Ordinal);
        var isSecond = string.Equals(secondResource, name, StringComparison.Ordinal);

        return isFirst || isSecond ? (isFirst, isSecond) : (true, true);
    }
}

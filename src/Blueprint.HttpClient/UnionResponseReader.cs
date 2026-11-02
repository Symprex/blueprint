using System.Text.Json;
using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.HttpClient;

/// <summary>
/// How one member of a <see cref="Results{T1, T2}"/> claims and reads a response: the statuses at which the host writes it
/// as a JSON body and how that body is read, the result it is when the host writes it with no body, whether it is a
/// <see cref="PlainTextResult"/>, and, for a resource event, the resource type name a body's <c>resourceObject</c> names.
/// </summary>
/// <remarks>
/// Members are made by <see cref="BlueprintResponseReaders"/>, and a union reader is made of two by
/// <see cref="BlueprintResponseReaders.Union{T1, T2}"/>.
/// </remarks>
/// <typeparam name="T">The member type.</typeparam>
public sealed class UnionMember<T>
{
    /// <summary>
    /// Initialises a new instance of the <see cref="UnionMember{T}"/> class.
    /// </summary>
    /// <param name="isJsonAt">Whether the host writes the member as a JSON body with a status.</param>
    /// <param name="readJson">Reads such a body as the member, or as <see langword="null"/> for none.</param>
    /// <param name="bodiless">The member a body-less response of a status is, or <see langword="null"/> when the host writes none so.</param>
    /// <param name="isPlainText">Whether the member is a <see cref="PlainTextResult"/>, which claims any body that is not JSON.</param>
    /// <param name="eventResourceTypeName">The resource type name of a resource event member, or <see langword="null"/> for any other.</param>
    internal UnionMember(
        Func<int, bool> isJsonAt,
        Func<BlueprintResponseContext, CancellationToken, Task<T?>> readJson,
        Func<int, T?>? bodiless,
        bool isPlainText,
        string? eventResourceTypeName)
    {
        this.IsJsonAt = isJsonAt;
        this.ReadJson = readJson;
        this.Bodiless = bodiless;
        this.IsPlainText = isPlainText;
        this.EventResourceTypeName = eventResourceTypeName;
    }

    /// <summary>
    /// Gets whether the host writes the member as a JSON body with a status.
    /// </summary>
    internal Func<int, bool> IsJsonAt { get; }

    /// <summary>
    /// Gets the read of such a body.
    /// </summary>
    internal Func<BlueprintResponseContext, CancellationToken, Task<T?>> ReadJson { get; }

    /// <summary>
    /// Gets the member a body-less response of a status is, when the host writes one so.
    /// </summary>
    internal Func<int, T?>? Bodiless { get; }

    /// <summary>
    /// Gets a value indicating whether the member is a <see cref="PlainTextResult"/>.
    /// </summary>
    internal bool IsPlainText { get; }

    /// <summary>
    /// Gets the resource type name of a resource event member.
    /// </summary>
    internal string? EventResourceTypeName { get; }
}

/// <summary>
/// Reads a successful response as the member of the <see cref="Results{T1, T2}"/> the host wrote. With no body, the first
/// member whose bodiless form it is claims it. With a body that is not JSON, the first <see cref="PlainTextResult"/> member
/// claims it. With a JSON body, the one member the host writes as a JSON body of that status claims it.
/// </summary>
/// <remarks>
/// When both members could be that JSON body, the body cannot say which one the host wrote, so rather than read it as
/// whichever is declared first this throws, naming them: a runtime guard, as the union is the caller's choice of response
/// type. Resource events of distinct resources are the exception: the body's <c>resourceObject</c> says which. A response
/// no member claims, a 204 included, is a contract mismatch, as a default union would hold no member at all; legacy's client
/// threw for any response it could not understand (Signature365 <c>8b40ecc961</c>,
/// <c>Common.Core/Api/BlueprintHttpClient.cs:228</c>).
/// </remarks>
/// <typeparam name="T1">The first member type.</typeparam>
/// <typeparam name="T2">The second member type.</typeparam>
internal sealed class UnionResponseReader<T1, T2> : BlueprintResponseReader<Results<T1, T2>>
    where T1 : notnull
    where T2 : notnull
{
    private readonly UnionMember<T1> _first;
    private readonly UnionMember<T2> _second;

    /// <summary>
    /// Initialises a new instance of the <see cref="UnionResponseReader{T1, T2}"/> class.
    /// </summary>
    /// <param name="first">The first member.</param>
    /// <param name="second">The second member.</param>
    internal UnionResponseReader(UnionMember<T1> first, UnionMember<T2> second)
    {
        this._first = first;
        this._second = second;
    }

    /// <inheritdoc />
    internal override async Task<Results<T1, T2>> ReadAsync(BlueprintResponseContext context, CancellationToken cancellationToken)
    {
        var response = context.Response;
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        var status = (int)response.StatusCode;

        if (mediaType is not null
            && (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)))
        {
            var first = this._first.IsJsonAt(status);
            var second = this._second.IsJsonAt(status);

            if (first && second && this._first.EventResourceTypeName is not null && this._second.EventResourceTypeName is not null)
            {
                (first, second) = await this.SelectEventOfResourceObjectAsync(response, cancellationToken).ConfigureAwait(false);
            }

            if (first && second)
            {
                throw new InvalidOperationException(
                    $"The response ({status}, {mediaType}) could be more than one of the results {typeof(Results<T1, T2>)} declares " +
                    $"({typeof(T1)}, {typeof(T2)}), so which the host wrote cannot be told.");
            }

            if (first && await this._first.ReadJson(context, cancellationToken).ConfigureAwait(false) is { } firstMember)
            {
                return firstMember;
            }

            if (second && await this._second.ReadJson(context, cancellationToken).ConfigureAwait(false) is { } secondMember)
            {
                return secondMember;
            }

            throw CreateNoMemberException(response);
        }

        if (await ReadNonJsonAsync(this._first, response, mediaType, status, cancellationToken).ConfigureAwait(false) is (true, { } firstNonJson))
        {
            return firstNonJson;
        }

        if (await ReadNonJsonAsync(this._second, response, mediaType, status, cancellationToken).ConfigureAwait(false) is (true, { } secondNonJson))
        {
            return secondNonJson;
        }

        throw CreateNoMemberException(response);
    }

    private static async Task<(bool Found, T? Member)> ReadNonJsonAsync<T>(
        UnionMember<T> member,
        HttpResponseMessage response,
        string? mediaType,
        int status,
        CancellationToken cancellationToken)
    {
        if (member.IsPlainText && mediaType is not null)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return (true, (T)(object)new PlainTextResult(text, response.StatusCode) { ContentType = mediaType });
        }

        return mediaType is null && member.Bodiless is not null && member.Bodiless(status) is { } bodiless ? (true, bodiless) : (false, default);
    }

    private static InvalidOperationException CreateNoMemberException(HttpResponseMessage response)
    {
        return new InvalidOperationException(
            $"The response ({(int)response.StatusCode}, {response.Content.Headers.ContentType?.MediaType ?? "no content"}) " +
            $"matches none of the results {typeof(Results<T1, T2>)} declares.");
    }

    /// <summary>
    /// Narrows two resource events that could each be the body to the one of the resource the body names. The host writes
    /// an event's <c>resourceObject</c> as its resource's type name (<see cref="ApiResource.GetTypeName"/>), fixed by the
    /// event's type, so two events of distinct resources, which the generator accepts in one union, are told apart by it, as
    /// the OpenAPI document's discriminator says for legacy events. When the body names neither, both stay candidates.
    /// </summary>
    private async Task<(bool First, bool Second)> SelectEventOfResourceObjectAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // Read as bytes, which buffers the content without consuming the stream the member is read from afterwards.
        using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));

        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("resourceObject", out var resourceObject)
            || resourceObject.ValueKind != JsonValueKind.String)
        {
            return (true, true);
        }

        var name = resourceObject.GetString();
        var first = string.Equals(this._first.EventResourceTypeName, name, StringComparison.Ordinal);
        var second = string.Equals(this._second.EventResourceTypeName, name, StringComparison.Ordinal);

        return first || second ? (first, second) : (true, true);
    }
}

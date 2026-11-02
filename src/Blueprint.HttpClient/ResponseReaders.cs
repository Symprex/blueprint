using System.Net;
using System.Text.Json;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;

namespace Blueprint.HttpClient;

/// <summary>
/// The response readers, one per result shape the host writes, and the union members they are built from. A caller that
/// knows its result type when it is compiled picks one here; <see cref="RuntimeResponseReaders"/> picks the same ones from a
/// type at run time.
/// </summary>
internal static class ResponseReaders
{
    /// <summary>
    /// Reads a JSON body as <typeparamref name="T"/>, a type the host writes as a plain JSON body.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <returns>The reader.</returns>
    internal static BlueprintResponseReader<T> Json<T>()
    {
        return new ValueResponseReader<T>(null, acceptsPlainText: false, isString: false, ReadJsonAsync<T>);
    }

    /// <summary>
    /// Reads a <c>text/*</c> body as its text, and any other as a JSON string.
    /// </summary>
    /// <returns>The reader.</returns>
    internal static BlueprintResponseReader<string> String()
    {
        return new ValueResponseReader<string>(null, acceptsPlainText: false, isString: true, ReadJsonAsync<string>);
    }

    /// <summary>
    /// Reads a body with a media type as a <see cref="PlainTextResult"/> of its text.
    /// </summary>
    /// <returns>The reader.</returns>
    internal static BlueprintResponseReader<PlainTextResult> PlainText()
    {
        return new ValueResponseReader<PlainTextResult>(null, acceptsPlainText: true, isString: false, ReadJsonAsync<PlainTextResult>);
    }

    /// <summary>
    /// Reads a result type the host can write with no body: <paramref name="noMediaTypeResult"/> gives the result a response
    /// of a status with no media type is.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="noMediaTypeResult">The result a body-less response of a status is, or <see langword="null"/> when there is none.</param>
    /// <param name="acceptsPlainText">Whether the result type can hold a <see cref="PlainTextResult"/>.</param>
    /// <returns>The reader.</returns>
    internal static BlueprintResponseReader<T> Bodiless<T>(Func<int, T?> noMediaTypeResult, bool acceptsPlainText)
    {
        return new ValueResponseReader<T>(noMediaTypeResult, acceptsPlainText, isString: false, ReadJsonAsync<T>);
    }

    /// <summary>
    /// Reads a resource event of <typeparamref name="TResource"/> as legacy read one (<see cref="ResourceEventReader"/>), a
    /// JSON <c>null</c> being a <c>response_empty</c> failure.
    /// </summary>
    /// <typeparam name="TEvent">The resource event type.</typeparam>
    /// <typeparam name="TResource">The event's resource type.</typeparam>
    /// <param name="create">Makes the event the body is read into.</param>
    /// <returns>The reader.</returns>
    internal static BlueprintResponseReader<TEvent> ResourceEvent<TEvent, TResource>(Func<TEvent> create)
        where TEvent : ResourceEvent<TResource>
        where TResource : ApiResource
    {
        return new ValueResponseReader<TEvent>(null, acceptsPlainText: false, isString: false, async (context, cancellationToken) =>
            await ResourceEventReader.ReadOrNullAsync<TEvent, TResource>(create, context.Response.Content, context.JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw await context.CreateResponseEmptyExceptionAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Reads the envelope the host writes for a <see cref="ResourceEventResult{T}"/>.
    /// </summary>
    /// <typeparam name="T">The event's content type.</typeparam>
    /// <returns>The reader.</returns>
    internal static BlueprintResponseReader<ResourceEventResult<T>> ResourceEventResult<T>()
        where T : notnull
    {
        return new ValueResponseReader<ResourceEventResult<T>>(null, acceptsPlainText: false, isString: false, ReadResourceEventResultAsync<T>);
    }

    /// <summary>
    /// Reads a nested <see cref="StatusCodeResult"/> as its singleton when the status is its own.
    /// </summary>
    /// <typeparam name="T">The nested status result type.</typeparam>
    /// <param name="instance">The type's singleton.</param>
    /// <returns>The reader.</returns>
    internal static BlueprintResponseReader<T> StatusCode<T>(T? instance)
        where T : StatusCodeResult
    {
        return new StatusCodeResultResponseReader<T>(instance);
    }

    /// <summary>
    /// Reads the member of a two-member union the host wrote.
    /// </summary>
    /// <typeparam name="T1">The first member type.</typeparam>
    /// <typeparam name="T2">The second member type.</typeparam>
    /// <param name="first">The first member.</param>
    /// <param name="second">The second member.</param>
    /// <returns>The reader.</returns>
    internal static BlueprintResponseReader<Results<T1, T2>> Union<T1, T2>(UnionMember<T1> first, UnionMember<T2> second)
        where T1 : notnull
        where T2 : notnull
    {
        return new UnionResponseReader<T1, T2>(first, second);
    }

    /// <summary>
    /// A union member the host writes as a plain JSON body of a 200: a type that is not an operation result, or a
    /// non-generic resource event.
    /// </summary>
    /// <typeparam name="T">The member type.</typeparam>
    /// <param name="eventResourceTypeName">
    /// The resource type name of a resource event that is read as plain JSON, a non-generic subclass of a
    /// <see cref="ResourceEvent{TResource}"/>, whose body's <c>resourceObject</c> names it; otherwise <see langword="null"/>.
    /// </param>
    /// <returns>The member.</returns>
    internal static UnionMember<T> JsonMember<T>(string? eventResourceTypeName = null)
    {
        return new UnionMember<T>(IsOk, ReadJsonOrNullAsync<T>, bodiless: null, isPlainText: false, eventResourceTypeName);
    }

    /// <summary>
    /// A union member that is a semantic result the host writes as its content at <paramref name="status"/>: an
    /// <see cref="OkResult{T}"/> at a 200, a <see cref="CreatedResult{T}"/> at a 201, an <see cref="AcceptedResult{T}"/> at a
    /// 202, read as its content wrapped by <paramref name="wrap"/>.
    /// </summary>
    /// <typeparam name="TResult">The member type.</typeparam>
    /// <typeparam name="TContent">The content type.</typeparam>
    /// <param name="status">The status the host writes the member with.</param>
    /// <param name="wrap">Wraps the content read in the member.</param>
    /// <returns>The member.</returns>
    internal static UnionMember<TResult> ContentMember<TResult, TContent>(int status, Func<TContent, TResult> wrap)
    {
        return new UnionMember<TResult>(
            s => s == status,
            async (context, cancellationToken) =>
                await ReadJsonOrNullAsync<TContent>(context, cancellationToken).ConfigureAwait(false) is { } content ? wrap(content) : default,
            bodiless: null,
            isPlainText: false,
            eventResourceTypeName: null);
    }

    /// <summary>
    /// A union member that is a resource event of <typeparamref name="TResource"/>, which the host writes as a 200 whatever
    /// its change type, as legacy's <c>OkResult</c> sent one.
    /// </summary>
    /// <typeparam name="TEvent">The resource event type.</typeparam>
    /// <typeparam name="TResource">The event's resource type.</typeparam>
    /// <param name="create">Makes the event the body is read into.</param>
    /// <returns>The member.</returns>
    internal static UnionMember<TEvent> ResourceEventMember<TEvent, TResource>(Func<TEvent> create)
        where TEvent : ResourceEvent<TResource>
        where TResource : ApiResource
    {
        return new UnionMember<TEvent>(
            IsOk,
            async (context, cancellationToken) =>
                await ResourceEventReader.ReadOrNullAsync<TEvent, TResource>(create, context.Response.Content, context.JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new JsonException("Unexpected end when reading JSON."),
            bodiless: null,
            isPlainText: false,
            ApiResource.GetTypeName(typeof(TResource)));
    }

    /// <summary>
    /// A union member that is a <see cref="ResourceEventResult{T}"/>, which the host writes as its envelope with the 201 or
    /// 200 of a created or updated one; a deleted one is an empty 204, which carries no content to read.
    /// </summary>
    /// <typeparam name="T">The event's content type.</typeparam>
    /// <returns>The member.</returns>
    internal static UnionMember<ResourceEventResult<T>> ResourceEventResultMember<T>()
        where T : notnull
    {
        return new UnionMember<ResourceEventResult<T>>(
            static s => s is (int)HttpStatusCode.OK or (int)HttpStatusCode.Created,
            async (context, cancellationToken) => await ReadResourceEventResultAsync<T>(context, cancellationToken).ConfigureAwait(false),
            bodiless: null,
            isPlainText: false,
            ApiResource.GetTypeName(typeof(T)));
    }

    /// <summary>
    /// A union member that is a <see cref="PlainTextResult"/>, which claims any body that is not JSON.
    /// </summary>
    /// <returns>The member.</returns>
    internal static UnionMember<PlainTextResult> PlainTextMember()
    {
        return new UnionMember<PlainTextResult>(static _ => false, ReadJsonOrNullAsync<PlainTextResult>, bodiless: null, isPlainText: true, eventResourceTypeName: null);
    }

    /// <summary>
    /// A union member the host writes with no body: <paramref name="bodiless"/> gives the member a body-less response of a
    /// status is. With none, the member claims no response: an operation result the host never writes back as a body.
    /// </summary>
    /// <typeparam name="T">The member type.</typeparam>
    /// <param name="bodiless">The member a body-less response of a status is, or <see langword="null"/> when there is none.</param>
    /// <returns>The member.</returns>
    internal static UnionMember<T> BodilessMember<T>(Func<int, T?>? bodiless)
    {
        return new UnionMember<T>(static _ => false, ReadJsonOrNullAsync<T>, bodiless, isPlainText: false, eventResourceTypeName: null);
    }

    private static bool IsOk(int status)
    {
        return status == (int)HttpStatusCode.OK;
    }

    private static Task<T> ReadJsonAsync<T>(BlueprintResponseContext context, CancellationToken cancellationToken)
    {
        return context.ReadJsonOrThrowAsync<T>(cancellationToken);
    }

    private static async Task<T?> ReadJsonOrNullAsync<T>(BlueprintResponseContext context, CancellationToken cancellationToken)
    {
        return await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<T>(context.Response.Content, context.JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the envelope the host writes for a <see cref="ResourceEventResult{T}"/>
    /// (<c>HttpOperationResultExecutor.WriteResourceEventEnvelopeAsync</c>): its <c>changeType</c>, <c>eventId</c>,
    /// <c>data</c> as the content, the <c>metadata</c>, <c>secureData</c> and <c>changedValues</c> the host writes when set,
    /// each read as a resource event's are (<see cref="ResourceEventReader.ReadDictionary"/>), and the
    /// <c>correlationId</c>. The envelope carries no resource id, so the result's is empty.
    /// </summary>
    /// <exception cref="InvalidOperationException">When the body is not an envelope with a change type and data.</exception>
    private static async Task<ResourceEventResult<T>> ReadResourceEventResultAsync<T>(BlueprintResponseContext context, CancellationToken cancellationToken)
        where T : notnull
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
        var result = new ResourceEventResult<T>(changeType, eventId, dataElement.Deserialize<T>(context.JsonOptions)!);

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

/// <summary>
/// The results the host writes with no body, made from the response's status alone.
/// </summary>
internal static class BodilessResults
{
    /// <summary>
    /// Every nested <see cref="StatusCodeResult"/> singleton, which a body-less response asked for as the base type is the
    /// one of its status of. <c>StatusCodeResultReadTests</c> holds the success ones to the nested types <see cref="StatusCodeResult"/> declares.
    /// </summary>
    internal static readonly StatusCodeResult[] StatusCodeResults =
    [
        StatusCodeResult.OK.Instance,
        StatusCodeResult.Created.Instance,
        StatusCodeResult.Accepted.Instance,
        StatusCodeResult.NoContent.Instance,
        StatusCodeResult.BadRequest.Instance,
        StatusCodeResult.NotImplemented.Instance,
        StatusCodeResult.ServiceUnavailable.Instance,
    ];

    /// <summary>
    /// The nested <see cref="StatusCodeResult"/> singleton of <paramref name="status"/>, as legacy's client made a
    /// <c>StatusCodeResult</c> of any status (Signature365 <c>8b40ecc961</c>, <c>Common.Core/Api/BlueprintHttpClient.cs:223-226</c>).
    /// </summary>
    /// <param name="status">The response's status.</param>
    /// <returns>The singleton, or <see langword="null"/> when no nested result has that status.</returns>
    internal static StatusCodeResult? AnyStatusCode(int status)
    {
        foreach (var result in StatusCodeResults)
        {
            if (result.StatusCode == status)
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>
    /// An <see cref="HttpNoContentResult"/>, which the host writes as an empty 204.
    /// </summary>
    /// <param name="status">The response's status.</param>
    /// <returns>The result, or <see langword="null"/> for another status.</returns>
    internal static HttpNoContentResult? HttpNoContent(int status)
    {
        return status == (int)HttpStatusCode.NoContent ? new HttpNoContentResult() : null;
    }

    /// <summary>
    /// The <see cref="NoResultOperationResult"/>, which the host writes as an empty 200.
    /// </summary>
    /// <param name="status">The response's status.</param>
    /// <returns>The result, or <see langword="null"/> for another status.</returns>
    internal static NoResultOperationResult? NoResult(int status)
    {
        return status == (int)HttpStatusCode.OK ? NoResultOperationResult.Instance : null;
    }

    /// <summary>
    /// An <see cref="HttpAcceptedResult"/>, which the host writes as an empty 202.
    /// </summary>
    /// <param name="status">The response's status.</param>
    /// <returns>The result, or <see langword="null"/> for another status.</returns>
    internal static HttpAcceptedResult? HttpAccepted(int status)
    {
        return status == (int)HttpStatusCode.Accepted ? new HttpAcceptedResult() : null;
    }

    /// <summary>
    /// A <see cref="KeyOnlyResourceEventResult"/>, whose change type the host writes as its status, and nothing else of it:
    /// a created one as a 201, an updated one as a 200 and a deleted one as a 204
    /// (<c>HttpOperationResultExecutor.MapChangeTypeToStatusCode</c>).
    /// </summary>
    /// <param name="status">The response's status.</param>
    /// <returns>The result, or <see langword="null"/> for another status.</returns>
    internal static KeyOnlyResourceEventResult? KeyOnlyResourceEvent(int status)
    {
        return status switch
        {
            (int)HttpStatusCode.Created => new KeyOnlyResourceEventResult(ChangeType.Created),
            (int)HttpStatusCode.OK => new KeyOnlyResourceEventResult(ChangeType.Updated),
            (int)HttpStatusCode.NoContent => new KeyOnlyResourceEventResult(ChangeType.Deleted),
            _ => null,
        };
    }

    /// <summary>
    /// A status-only <see cref="HttpResult"/>, for a body-less response asked for as the base <see cref="HttpResult"/>, which
    /// is a sibling of <see cref="StatusCodeResult"/> here where legacy's <c>StatusCodeResult</c> derived from it.
    /// </summary>
    /// <param name="status">The response's status.</param>
    /// <returns>The result.</returns>
    internal static HttpResult StatusOnlyHttpResult(int status)
    {
        return new BodilessHttpResult((HttpStatusCode)status);
    }

    /// <summary>
    /// A client-materialised <see cref="HttpResult"/> carrying only a status. Its <see cref="HttpResult.ExecuteAsync"/> (the
    /// base implementation — this type adds nothing) writes exactly the status and no body, so a gateway handler that
    /// forwards this value unchanged answers its own caller with the same bodiless response.
    /// </summary>
    private sealed class BodilessHttpResult : HttpResult
    {
        public BodilessHttpResult(HttpStatusCode statusCode)
            : base(statusCode)
        {
        }
    }
}

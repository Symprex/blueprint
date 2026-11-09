using System.Net;
using System.Reflection;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.HttpClient.Readers;

/// <summary>
/// Chooses the reader <see cref="BlueprintHttpClient.ForwardAsync{TInner, TResult}(TInner, CallOptions, CancellationToken)"/> reads a forward's declared result with:
/// the reader <see cref="AppResponseReaders"/> chooses, made once per result type, except that a
/// <see cref="Results{T1, T2}"/> with a <see cref="NotFoundResult{T}"/> member also claims the 404 the host writes for that
/// member.
/// </summary>
/// <remarks>
/// A typed client's own calls are unchanged: their readers claim no failure, so a 404 still fails as a
/// <see cref="BlueprintApiException"/> there.
/// </remarks>
internal static class ForwardResponseReaders
{
    /// <summary>
    /// Gets the reader of <typeparamref name="T"/> for a forward, constructing it on first use.
    /// </summary>
    /// <typeparam name="T">The result the forward declares.</typeparam>
    /// <returns>The reader, shared by every forward declaring <typeparamref name="T"/>.</returns>
    internal static ResponseReader<T> For<T>()
    {
        return Cache<T>.Reader;
    }

    private static ResponseReader<T> Create<T>()
    {
        var type = typeof(T);

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Results<,>))
        {
            var members = type.GetGenericArguments();

            for (var index = 0; index < members.Length; index++)
            {
                if (members[index].IsGenericType && members[index].GetGenericTypeDefinition() == typeof(NotFoundResult<>))
                {
                    // Unwrapped, so a reader's refusal of its type reaches the caller as itself.
                    return (ResponseReader<T>)typeof(ForwardResponseReaders)
                        .GetMethod(nameof(CreateNotFoundReader), BindingFlags.NonPublic | BindingFlags.Static)!
                        .MakeGenericMethod(members[0], members[1], members[index].GetGenericArguments()[0])
                        .Invoke(null, BindingFlags.DoNotWrapExceptions, binder: null, parameters: [index == 0], culture: null)!;
                }
            }
        }

        return AppResponseReaders.For<T>();
    }

    private static NotFoundForwardResponseReader<T1, T2, TContent> CreateNotFoundReader<T1, T2, TContent>(bool notFoundIsFirst)
        where T1 : notnull
        where T2 : notnull
        where TContent : notnull
    {
        return new NotFoundForwardResponseReader<T1, T2, TContent>(AppResponseReaders.For<Results<T1, T2>>(), notFoundIsFirst);
    }

    /// <summary>
    /// Holds the reader of <typeparamref name="T"/>, made on first use. A failure to make one is not remembered, so it is
    /// raised again, as itself, by the next call.
    /// </summary>
    private static class Cache<T>
    {
        private static ResponseReader<T>? _reader;

        internal static ResponseReader<T> Reader => LazyInitializer.EnsureInitialized(ref _reader, static () => Create<T>());
    }
}

/// <summary>
/// Reads a forward's <see cref="Results{T1, T2}"/> with a <see cref="NotFoundResult{T}"/> member: a success as the union's own
/// reader reads it, and a 404 with a JSON body (not a problem document) as that member, its content read from the body, as
/// <c>HttpOperationResultExecutor</c> writes a <see cref="NotFoundResult{T}"/>.
/// </summary>
/// <remarks>
/// A 404 problem document is a thrown not-found, not the member's content, so it is left to the transport and escapes as the
/// <see cref="BlueprintApiException"/> it maps, the upstream problem unchanged.
/// </remarks>
/// <typeparam name="T1">The union's first member type.</typeparam>
/// <typeparam name="T2">The union's second member type.</typeparam>
/// <typeparam name="TContent">The not-found member's content type.</typeparam>
internal sealed class NotFoundForwardResponseReader<T1, T2, TContent> : ResponseReader<Results<T1, T2>>
    where T1 : notnull
    where T2 : notnull
    where TContent : notnull
{
    private readonly ResponseReader<Results<T1, T2>> _union;
    private readonly bool _notFoundIsFirst;

    /// <summary>
    /// Initialises a new instance of the <see cref="NotFoundForwardResponseReader{T1, T2, TContent}"/> class.
    /// </summary>
    /// <param name="union">The reader of a success, chosen for the union as for any call.</param>
    /// <param name="notFoundIsFirst">Whether the not-found member is <typeparamref name="T1"/>, otherwise <typeparamref name="T2"/>.</param>
    internal NotFoundForwardResponseReader(ResponseReader<Results<T1, T2>> union, bool notFoundIsFirst)
    {
        this._union = union;
        this._notFoundIsFirst = notFoundIsFirst;
    }

    /// <inheritdoc />
    internal override string? EventResourceObject => this._union.EventResourceObject;

    /// <inheritdoc />
    internal override ValueTask<Results<T1, T2>> ReadAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        return this._union.ReadAsync(context, cancellationToken);
    }

    /// <inheritdoc />
    internal override bool IsJsonBodyOfStatus(int status)
    {
        return this._union.IsJsonBodyOfStatus(status);
    }

    /// <inheritdoc />
    internal override ValueTask<(bool Read, Results<T1, T2> Value)> ReadJsonMemberAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        return this._union.ReadJsonMemberAsync(context, cancellationToken);
    }

    /// <inheritdoc />
    internal override ValueTask<(bool Read, Results<T1, T2> Value)> ReadOtherMemberAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        return this._union.ReadOtherMemberAsync(context, cancellationToken);
    }

    /// <inheritdoc />
    internal override async ValueTask<(bool Read, Results<T1, T2> Value)> ReadFailureAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        if (context.Response.StatusCode != HttpStatusCode.NotFound || !IsResourceJson(context.MediaType))
        {
            return default;
        }

        // Read as bytes, which buffers the content, so a body this does not claim is still there for the transport to map.
        var body = await context.Response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        TContent? content;

        try
        {
            content = JsonSerializer.Deserialize<TContent>(body, context.JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }

        if (content is null)
        {
            return default;
        }

        var notFound = new NotFoundResult<TContent>(content);

        return (true, this._notFoundIsFirst ? (T1)(object)notFound : (T2)(object)notFound);
    }

    /// <summary>
    /// Whether a 404's media type is a not-found result's content: JSON, but not <c>application/problem+json</c>, which a
    /// forward asks a host's errors to be written as (<see cref="BlueprintHttpClient.ForwardAsync{TInner, TResult}(TInner, CallOptions, CancellationToken)"/>).
    /// </summary>
    private static bool IsResourceJson(string? mediaType)
    {
        return mediaType is not null
               && !mediaType.Equals("application/problem+json", StringComparison.OrdinalIgnoreCase)
               && (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                   || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
    }
}

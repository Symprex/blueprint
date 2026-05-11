using Blueprint.Runtime.Exec;
using Microsoft.Extensions.Primitives;

namespace Blueprint.Http;

/// <summary>
/// Provides ETag-based conditional request handling for Blueprint HTTP operations.
/// Inject this into handlers that need to support <c>If-None-Match</c> / 304 Not Modified semantics.
/// </summary>
/// <remarks>
/// <para>
/// When a client sends an <c>If-None-Match</c> header whose value matches the ETag of the current
/// representation, this helper short-circuits content generation and returns a 304 Not Modified result.
/// Otherwise the full content is generated, wrapped in an <see cref="OkResult{T}"/>, and annotated
/// with <c>ETag</c> and <c>Cache-Control</c> headers.
/// </para>
/// <para>
/// ETag matching uses ordinal (case-sensitive) comparison against all values in the header,
/// supporting both single ETags and comma-separated lists.
/// </para>
/// </remarks>
/// <param name="contextAccessor">The HTTP context accessor used to read request headers.</param>
public sealed class ETagger(IHttpContextAccessor contextAccessor)
{
    private static readonly Dictionary<string, StringValues> NotModifiedCacheHeaders =
        new(StringComparer.Ordinal) { ["Cache-Control"] = "private, max-age=3600" };

    /// <summary>
    /// Wraps a computed value in an <see cref="OkResult{T}"/> while applying ETag conditional request semantics.
    /// </summary>
    /// <typeparam name="T">The response content type.</typeparam>
    /// <param name="eTag">The ETag value for the current representation.</param>
    /// <param name="createWhenNoMatch">Creates the response content when the request does not match the ETag.</param>
    /// <returns>
    /// A 200 OK result (including <c>ETag</c> and <c>Cache-Control</c> headers) when the request does not match,
    /// or a 304 Not Modified result when the request <c>If-None-Match</c> header contains an exact match.
    /// </returns>
    public OkResult<T> WithETag<T>(string eTag, Func<T> createWhenNoMatch) where T : notnull
    {
        var request = contextAccessor.HttpContext!.Request;

        if (RequestHasETagMatch(request, eTag))
        {
            return OkResult<T>.NotModified(eTag, NotModifiedCacheHeaders);
        }

        var result = createWhenNoMatch();

        return OkResult.For(
            result,
            new Dictionary<string, StringValues>(2, StringComparer.Ordinal)
            {
                ["ETag"] = eTag,
                ["Cache-Control"] = "private, max-age=3600",
            });
    }

    private static bool RequestHasETagMatch(HttpRequest request, string eTag)
    {
        if (!request.Headers.TryGetValue("If-None-Match", out var ifNoneMatchValues))
        {
            return false;
        }

        foreach (var headerValue in ifNoneMatchValues)
        {
            var remaining = (headerValue ?? string.Empty).AsSpan();

            while (!remaining.IsEmpty)
            {
                var commaIndex = remaining.IndexOf(',');
                var segment = commaIndex >= 0 ? remaining[..commaIndex] : remaining;
                remaining = commaIndex >= 0 ? remaining[(commaIndex + 1)..] : ReadOnlySpan<char>.Empty;

                var candidate = segment.Trim();

                if (candidate.Equals(eTag.AsSpan(), StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

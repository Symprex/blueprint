using System.Net.Http.Headers;
using Blueprint.Runtime.Exec;

namespace Blueprint;

/// <summary>
/// Extension methods for <see cref="OkResult"/> to enable fluent HTTP response configuration
/// such as setting cache control, ETag, or custom response headers.
/// </summary>
public static class OkResultExtensions
{
    /// <summary>
    /// Sets the <c>Cache-Control</c> response header on the result.
    /// </summary>
    /// <typeparam name="T">The result type (must derive from <see cref="OkResult"/>).</typeparam>
    /// <param name="result">The result to configure.</param>
    /// <param name="cacheControl">The cache control header value.</param>
    /// <returns>The same result for chaining.</returns>
    public static T WithCacheControl<T>(this T result, CacheControlHeaderValue cacheControl)
        where T : OkResult
    {
        ArgumentNullException.ThrowIfNull(cacheControl);

        result.Headers["Cache-Control"] = cacheControl.ToString();
        return result;
    }

    /// <summary>
    /// Sets the <c>ETag</c> response header on the result.
    /// </summary>
    /// <typeparam name="T">The result type (must derive from <see cref="OkResult"/>).</typeparam>
    /// <param name="result">The result to configure.</param>
    /// <param name="eTag">The ETag value (with or without surrounding quotes).</param>
    /// <returns>The same result for chaining.</returns>
    public static T WithETag<T>(this T result, string eTag)
        where T : OkResult
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eTag);

        result.Headers["ETag"] = eTag;
        return result;
    }

    /// <summary>
    /// Adds a custom response header to the result.
    /// </summary>
    /// <typeparam name="T">The result type (must derive from <see cref="OkResult"/>).</typeparam>
    /// <param name="result">The result to configure.</param>
    /// <param name="name">The header name.</param>
    /// <param name="value">The header value.</param>
    /// <returns>The same result for chaining.</returns>
    public static T WithHeader<T>(this T result, string name, string value)
        where T : OkResult
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        result.Headers[name] = value;
        return result;
    }

    /// <summary>
    /// Sets <c>Cache-Control: max-age={seconds}</c> on the result for simple duration-based caching.
    /// </summary>
    /// <typeparam name="T">The result type (must derive from <see cref="OkResult"/>).</typeparam>
    /// <param name="result">The result to configure.</param>
    /// <param name="seconds">The maximum age in seconds before the response is considered stale.</param>
    /// <returns>The same result for chaining.</returns>
    public static T WithMaxAge<T>(this T result, int seconds)
        where T : OkResult
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);

        result.Headers["Cache-Control"] = $"max-age={seconds}";
        return result;
    }

    /// <summary>
    /// Sets <c>Cache-Control: no-store</c> on the result, preventing any caching of the response.
    /// </summary>
    /// <typeparam name="T">The result type (must derive from <see cref="OkResult"/>).</typeparam>
    /// <param name="result">The result to configure.</param>
    /// <returns>The same result for chaining.</returns>
    public static T WithNoStore<T>(this T result)
        where T : OkResult
    {
        result.Headers["Cache-Control"] = "no-store";
        return result;
    }
}

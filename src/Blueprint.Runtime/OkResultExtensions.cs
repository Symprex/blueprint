using System.Net.Http.Headers;
using Blueprint.Runtime.Exec;

namespace Blueprint;

/// <summary>
/// Extension methods for <see cref="OkResult"/> to enable fluent HTTP response configuration
/// such as setting cache control or custom response headers.
/// </summary>
public static class OkResultExtensions
{
    /// <summary>
    /// Sets the Cache-Control header on the result.
    /// </summary>
    /// <typeparam name="T">The result type (must derive from <see cref="OkResult"/>).</typeparam>
    /// <param name="result">The result to configure.</param>
    /// <param name="cacheControl">The cache control header value.</param>
    /// <returns>The same result for chaining.</returns>
    public static T WithCacheControl<T>(this T result, CacheControlHeaderValue cacheControl)
        where T : OkResult
    {
        result.Headers["Cache-Control"] = cacheControl.ToString();
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
        result.Headers[name] = value;
        return result;
    }
}

using System.Reflection;
using Blueprint.Runtime.Artifacts;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Builds the development-only markdown inspection endpoint that serves prebuilt markdown bytes exactly as they were
/// embedded, so richer hosted views can widen without moving markdown projection logic into <c>Blueprint.Runtime</c>.
/// </summary>
internal static class ManifestMarkdownInspectionEndpoint
{
    /// <summary>
    /// Creates the request delegate that serves the configured markdown manifest resource.
    /// </summary>
    /// <param name="assembly">The assembly that owns the embedded markdown resource.</param>
    /// <param name="resourceName">The exact embedded resource name to load for the hosted markdown view.</param>
    /// <returns>A request delegate that writes the prebuilt markdown projection to the response.</returns>
    public static RequestDelegate Create(Assembly assembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (string.IsNullOrWhiteSpace(resourceName))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(resourceName));
        }

        var markdown = ManifestResourceLoader.LoadRequiredString(assembly, resourceName);

        return async httpContext =>
        {
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "text/markdown; charset=utf-8";
            await httpContext.Response.WriteAsync(markdown, httpContext.RequestAborted);
        };
    }
}

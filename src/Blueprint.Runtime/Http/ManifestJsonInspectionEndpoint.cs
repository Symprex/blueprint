using System.Reflection;
using Blueprint.Runtime.Artifacts;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Builds the development-only JSON inspection endpoint that exposes the canonical manifest bytes exactly as they were
/// embedded, so hosted inspection surfaces can reuse build output without adding a second manifest interpretation layer
/// inside the runtime assembly.
/// </summary>
internal static class ManifestJsonInspectionEndpoint
{
    /// <summary>
    /// Creates the request delegate that serves the configured manifest resource.
    /// </summary>
    /// <param name="assembly">The assembly that owns the embedded manifest resource.</param>
    /// <param name="resourceName">The exact embedded resource name to load for the hosted manifest view.</param>
    /// <returns>A request delegate that writes the canonical manifest JSON to the response.</returns>
    public static RequestDelegate Create(Assembly assembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (string.IsNullOrWhiteSpace(resourceName))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(resourceName));
        }

        var json = ManifestResourceLoader.LoadRequiredString(assembly, resourceName);

        return async httpContext =>
        {
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "application/json; charset=utf-8";
            await httpContext.Response.WriteAsync(json, httpContext.RequestAborted);
        };
    }
}

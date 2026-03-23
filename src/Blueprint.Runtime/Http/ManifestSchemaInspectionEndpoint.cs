using System.Reflection;
using Blueprint.Runtime.Artifacts;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Builds the development-only schema inspection endpoint that serves the approved manifest schema bytes exactly as they
/// were embedded, so hosted tooling can discover the contract without teaching <c>Blueprint.Runtime</c> how to interpret
/// schema semantics itself.
/// </summary>
internal static class ManifestSchemaInspectionEndpoint
{
    /// <summary>
    /// Creates the request delegate that serves the configured schema resource.
    /// </summary>
    /// <param name="assembly">The assembly that owns the embedded schema resource.</param>
    /// <param name="resourceName">The exact embedded resource name to load for the hosted schema view.</param>
    /// <returns>A request delegate that writes the embedded schema bytes to the response unchanged.</returns>
    public static RequestDelegate Create(Assembly assembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (string.IsNullOrWhiteSpace(resourceName))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(resourceName));
        }

        var schema = ManifestResourceLoader.LoadRequiredString(assembly, resourceName);

        return async httpContext =>
        {
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "application/schema+json; charset=utf-8";
            await httpContext.Response.WriteAsync(schema, httpContext.RequestAborted);
        };
    }
}

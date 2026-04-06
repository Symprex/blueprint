namespace Blueprint.Runtime.Http;

/// <summary>
/// Provides access to the current HTTP context for pipeline stages that need to interact
/// with the underlying HTTP request or response.
/// </summary>
public interface IHttpOperationContext
{
    /// <summary>
    /// Gets the <see cref="HttpContext"/> for the current HTTP operation execution.
    /// </summary>
    HttpContext HttpContext { get; }
}

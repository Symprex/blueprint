using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.Http;

/// <summary>
/// A resource link generator adds links to a <see cref="ILinkableResource"/> at runtime,
/// enabling dynamic link population based on operation context (e.g. user permissions,
/// resource state, or conditional relationships).
/// </summary>
/// <remarks>
/// Register implementations via DI. The generated pipeline will resolve all registered
/// <see cref="IResourceLinkGenerator"/> instances and invoke them on the operation result
/// when the result implements <see cref="ILinkableResource"/>.
/// </remarks>
public interface IResourceLinkGenerator
{
    /// <summary>
    /// Adds links to the given <paramref name="linkableResource"/> using the
    /// <paramref name="apiLinkGenerator"/> to construct URLs and the <paramref name="context"/>
    /// for access to user context or other runtime state.
    /// </summary>
    /// <param name="apiLinkGenerator">The link generator for constructing URLs.</param>
    /// <param name="context">The current operation execution context.</param>
    /// <param name="linkableResource">The resource to add links to.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    ValueTask AddLinksAsync(IApiLinkGenerator apiLinkGenerator, ApiOperationContext context, ILinkableResource linkableResource);
}

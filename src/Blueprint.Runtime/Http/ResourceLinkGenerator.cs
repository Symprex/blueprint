using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.Http;

/// <summary>
/// A convenience base class for <see cref="IResourceLinkGenerator"/> that checks the resource
/// type and delegates to a strongly-typed method. Only resources of type <typeparamref name="T"/>
/// are processed; all others are ignored.
/// </summary>
/// <typeparam name="T">The resource type this generator handles.</typeparam>
public abstract class ResourceLinkGenerator<T> : IResourceLinkGenerator
    where T : class, ILinkableResource
{
    /// <inheritdoc />
    public ValueTask AddLinksAsync(IApiLinkGenerator apiLinkGenerator, ApiOperationContext context, ILinkableResource linkableResource)
    {
        if (linkableResource is not T typedResource)
        {
            return default;
        }

        return this.AddLinksAsync(apiLinkGenerator, context, typedResource);
    }

    /// <summary>
    /// Adds links to the given strongly-typed <paramref name="resource"/>.
    /// </summary>
    /// <param name="apiLinkGenerator">The link generator for constructing URLs.</param>
    /// <param name="context">The current operation execution context.</param>
    /// <param name="resource">The typed resource to add links to.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    protected abstract ValueTask AddLinksAsync(IApiLinkGenerator apiLinkGenerator, ApiOperationContext context, T resource);
}

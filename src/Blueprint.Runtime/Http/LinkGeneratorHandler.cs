using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.Http;

/// <summary>
/// Static handler that orchestrates link population on operation results. Resolves
/// <see cref="IResourceLinkGenerator"/> instances from DI and invokes them on the
/// operation result, handling both single resources and collections.
/// </summary>
public static class LinkGeneratorHandler
{
    /// <summary>
    /// Adds links to the result of an operation by invoking all registered
    /// <see cref="IResourceLinkGenerator"/> instances.
    /// </summary>
    /// <param name="apiLinkGenerator">The link generator for URL construction.</param>
    /// <param name="registeredGenerators">All registered resource link generators.</param>
    /// <param name="context">The current operation context.</param>
    /// <param name="result">The operation result to enrich with links.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous link population.</returns>
    public static ValueTask AddLinksAsync(
        IApiLinkGenerator apiLinkGenerator,
        IEnumerable<IResourceLinkGenerator> registeredGenerators,
        ApiOperationContext context,
        OperationResult result)
    {
        var content = result switch
        {
            OkResult okResult => okResult.Content,
            ResourceEventResult resourceEventResult => resourceEventResult.Content,
            ResourceEvent resourceEvent => resourceEvent.Data,
            _ => null,
        };

        return content is not null
            ? AddLinksAsyncCore(apiLinkGenerator, registeredGenerators, context, result, content)
            : default;
    }

    private static async ValueTask AddLinksAsyncCore(
        IApiLinkGenerator apiLinkGenerator,
        IEnumerable<IResourceLinkGenerator> registeredGenerators,
        ApiOperationContext context,
        OperationResult result,
        object content)
    {
        var enriched = await AddResourceLinksAsync(apiLinkGenerator, registeredGenerators, context, content).ConfigureAwait(false);

        // OkResult allows content replacement (e.g. when materialising IQueryable)
        if (result is OkResult ok && !ReferenceEquals(ok.Content, enriched))
        {
            ok.Content = enriched;
        }
    }

    private static async ValueTask<object> AddResourceLinksAsync(
        IApiLinkGenerator apiLinkGenerator,
        IEnumerable<IResourceLinkGenerator> generators,
        ApiOperationContext context,
        object resource)
    {
        if (resource is ILinkableResource linkableResource)
        {
            await AddLinksAsync(apiLinkGenerator, generators, context, linkableResource).ConfigureAwait(false);
        }

        // Determine enumerable surface: IApiResourceCollection (paged/list) takes priority,
        // otherwise fall back to IEnumerable<object> for raw collections.
        var enumerableResult = resource is IApiResourceCollection pagedResult
            ? pagedResult.GetEnumerable()
            : resource as IEnumerable<object>;

        if (enumerableResult != null)
        {
            if (enumerableResult is IQueryable<object>)
            {
                // Materialise deferred queries so link mutations are preserved on the actual return
                enumerableResult = enumerableResult.ToList();
                resource = enumerableResult;
            }

            foreach (var obj in enumerableResult)
            {
                if (obj is ILinkableResource or IApiResourceCollection)
                {
                    // Recurse rather than call the ILinkableResource overload directly: an
                    // enumerated item may itself be a resource collection (e.g. a ListApiResource
                    // nested inside another collection), whose own children also need links.
                    await AddResourceLinksAsync(apiLinkGenerator, generators, context, obj).ConfigureAwait(false);
                }
                else
                {
                    // If the first item is not linkable, assume all items are the same type
                    break;
                }
            }
        }

        return resource;
    }

    private static async ValueTask AddLinksAsync(
        IApiLinkGenerator apiLinkGenerator,
        IEnumerable<IResourceLinkGenerator> generators,
        ApiOperationContext context,
        ILinkableResource result)
    {
        // Individual IResourceLinkGenerator implementations (e.g. EntityOperationResourceLinkGenerator)
        // carry their own ILogger for per-link trace diagnostics, so no logger resolution needed here.
        foreach (var resourceLinkGenerator in generators)
        {
            await resourceLinkGenerator.AddLinksAsync(apiLinkGenerator, context, result).ConfigureAwait(false);
        }
    }
}

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
    /// <returns>A task representing the asynchronous link population.</returns>
    public static async Task AddLinksAsync(
        IApiLinkGenerator apiLinkGenerator,
        IEnumerable<IResourceLinkGenerator> registeredGenerators,
        ApiOperationContext context,
        OperationResult result)
    {
        var content = result switch
        {
            OkResult okResult => okResult.Content,
            ResourceEventResult resourceEventResult => resourceEventResult.Content,
            _ => null,
        };

        if (content is null)
        {
            return;
        }

        var logger = context.ServiceProvider.GetRequiredService<ILogger<EntityOperationResourceLinkGenerator>>();

        var enriched = await AddResourceLinksAsync(logger, apiLinkGenerator, registeredGenerators, context, content);

        // OkResult allows content replacement (e.g. when materialising IQueryable)
        if (result is OkResult ok && !ReferenceEquals(ok.Content, enriched))
        {
            ok.Content = enriched;
        }
    }

    private static async ValueTask<object> AddResourceLinksAsync(
        ILogger logger,
        IApiLinkGenerator apiLinkGenerator,
        IEnumerable<IResourceLinkGenerator> generators,
        ApiOperationContext context,
        object resource)
    {
        if (resource is ILinkableResource linkableResource)
        {
            await AddLinksAsync(logger, apiLinkGenerator, generators, context, linkableResource);
        }

        var enumerableResult = resource as IEnumerable<object>;

        if (resource is IApiResourceCollection pagedResult)
        {
            enumerableResult = pagedResult.GetEnumerable();
        }

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
                if (obj is ILinkableResource apiResourceItem)
                {
                    await AddLinksAsync(logger, apiLinkGenerator, generators, context, apiResourceItem);
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
        ILogger logger,
        IApiLinkGenerator apiLinkGenerator,
        IEnumerable<IResourceLinkGenerator> generators,
        ApiOperationContext context,
        ILinkableResource result)
    {
        foreach (var resourceLinkGenerator in generators)
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace(
                    "Generating links. generator={GeneratorType} result_type={ResourceType}",
                    resourceLinkGenerator.GetType().Name,
                    result.GetType().Name);
            }

            await resourceLinkGenerator.AddLinksAsync(apiLinkGenerator, context, result);
        }
    }
}

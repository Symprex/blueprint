using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.Http;

/// <summary>
/// The default <see cref="IResourceLinkGenerator"/> that automatically populates all registered
/// entity operation links on a resource. For each <see cref="ApiOperationLink"/> registered against
/// the resource's type, this generator checks whether the link's operation is exposed, whether the
/// user is authorised to see it, and whether a URL can be constructed — only adding the link
/// if all conditions are met.
/// </summary>
public sealed class EntityOperationResourceLinkGenerator : IResourceLinkGenerator
{
    private readonly IApiAuthoriserAggregator _apiAuthoriserAggregator;
    private readonly ILogger<EntityOperationResourceLinkGenerator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityOperationResourceLinkGenerator"/> class.
    /// </summary>
    /// <param name="apiAuthoriserAggregator">The authoriser to determine link visibility.</param>
    /// <param name="logger">The logger.</param>
    public EntityOperationResourceLinkGenerator(
        IApiAuthoriserAggregator apiAuthoriserAggregator,
        ILogger<EntityOperationResourceLinkGenerator> logger)
    {
        Guard.NotNull(apiAuthoriserAggregator);
        Guard.NotNull(logger);

        this._apiAuthoriserAggregator = apiAuthoriserAggregator;
        this._logger = logger;
    }

    /// <inheritdoc/>
    public async ValueTask AddLinksAsync(IApiLinkGenerator apiLinkGenerator, ApiOperationContext context, ILinkableResource linkableResource)
    {
        Guard.NotNull(context);
        Guard.NotNull(linkableResource);

        var traceLogEnabled = this._logger.IsEnabled(LogLevel.Trace);
        var resourceType = linkableResource.GetType();
        var links = context.App.GetLinksForResource(resourceType);

        if (links.Count == 0)
        {
            if (traceLogEnabled)
            {
                this._logger.LogTrace(
                    "No links have been registered for linkable resource {ResourceType}",
                    resourceType);
            }

            return;
        }

        if (traceLogEnabled)
        {
            this._logger.LogTrace(
                "Attempting to add {LinkCount} links for the linkable resource {ResourceType}.",
                links.Count,
                resourceType);
        }

        foreach (var link in links)
        {
            var entityOperation = link.OperationDescriptor;
            var entityOperationName = entityOperation.OperationType.Name;

            if (!entityOperation.IsExposed)
            {
                if (traceLogEnabled)
                {
                    this._logger.LogTrace("Operation not exposed, excluding. operation_type={OperationType}", entityOperationName);
                }

                continue;
            }

            var result = await this._apiAuthoriserAggregator.CanShowLinkAsync(context, entityOperation, linkableResource).ConfigureAwait(false);

            if (result.IsAllowed == false)
            {
                if (traceLogEnabled)
                {
                    this._logger.LogTrace("Operation is not allowed to be executed, excluding. operation_type={OperationType}", entityOperationName);
                }

                continue;
            }

            var url = apiLinkGenerator.CreateUrl(link, linkableResource);

            // We could not generate this URL — a placeholder value could not be injected
            if (url == null)
            {
                if (traceLogEnabled)
                {
                    this._logger.LogTrace("Cannot add link, URL was not generated. operation_type={OperationType}", entityOperationName);
                }

                continue;
            }

            if (traceLogEnabled)
            {
                this._logger.LogTrace("All checks passed. Adding link. operation_type={OperationType}", entityOperationName);
            }

            // Skip if this rel was already populated — the generated pipeline may have added
            // a compile-time self link before the runtime link generator runs.
            if (linkableResource.Links.ContainsKey(link.Rel))
            {
                if (traceLogEnabled)
                {
                    this._logger.LogTrace("Link rel '{Rel}' already present on resource, skipping. operation_type={OperationType}", link.Rel, entityOperationName);
                }

                continue;
            }

            linkableResource.AddLink(link.Rel, new Link
            {
                Href = url,
            });
        }
    }
}

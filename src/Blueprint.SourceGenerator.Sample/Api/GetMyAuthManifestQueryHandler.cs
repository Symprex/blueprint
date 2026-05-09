using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles the auth manifest query by iterating all registered links and checking which ones
/// the current user is authorized to see. This demonstrates the <see cref="IApiAuthoriserAggregator"/>
/// pattern for building client-facing permission manifests.
/// </summary>
public class GetMyAuthManifestQueryHandler
{
    /// <summary>
    /// Queries all registered links in the <see cref="BlueprintApp"/> and returns only those
    /// the current user is authorized to access, producing a manifest that SPAs can use to
    /// conditionally render UI features.
    /// </summary>
    /// <param name="query">The auth manifest query operation.</param>
    /// <param name="context">The current operation execution context.</param>
    /// <param name="dataModel">The Blueprint app model containing all registered links.</param>
    /// <param name="authoriserAggregator">The aggregator that evaluates all registered authorisers.</param>
    /// <param name="logger">Logger for warning on authorisation evaluation errors.</param>
    /// <returns>An <see cref="AuthManifestApiResource"/> listing accessible operation names.</returns>
    public async Task<AuthManifestApiResource> HandleAsync(
        GetMyAuthManifestQuery query,
        ApiOperationContext context,
        BlueprintApp dataModel,
        IApiAuthoriserAggregator authoriserAggregator,
        ILogger<GetMyAuthManifestQueryHandler> logger)
    {
        var authManifest = new AuthManifestApiResource();

        foreach (var link in dataModel.Links)
        {
            if (!link.OperationDescriptor.IsExposed)
            {
                continue;
            }

            ExecutionAllowed checkResult;

            try
            {
                checkResult = await authoriserAggregator.CanShowLinkAsync(context, link.OperationDescriptor, null);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(
                    ex,
                    "Skipping auth manifest operation '{OperationName}' due to authorisation evaluation error.",
                    link.OperationDescriptor.Name);
                continue;
            }

            if (checkResult.IsAllowed)
            {
                authManifest.AccessibleOperations.Add(link.OperationDescriptor.Name);
            }
        }

        return authManifest;
    }
}

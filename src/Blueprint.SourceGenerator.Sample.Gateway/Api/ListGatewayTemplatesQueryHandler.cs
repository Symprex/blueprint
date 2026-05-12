namespace Blueprint.SourceGenerator.Sample.Gateway.Api;

/// <summary>
/// Gateway handler that delegates to the upstream template service via <c>BlueprintHttpClient</c>.
/// Demonstrates the typed HTTP client's ability to send a query operation to a remote Blueprint
/// service and deserialize the response into the upstream resource type, then map it to the gateway's
/// own resource contract.
/// </summary>
public class ListGatewayTemplatesQueryHandler(BlueprintHttpClient<SampleBlueprintApp> upstreamClient)
{
    /// <summary>
    /// Fetches all templates from the upstream service by sending a <see cref="GetTemplatesQuery"/>
    /// and mapping the results to <see cref="GatewayTemplateResource"/> instances.
    /// </summary>
    /// <param name="query">The gateway query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An array of gateway template resources mapped from the upstream response.</returns>
    public async Task<GatewayTemplateResource[]> Handle(ListGatewayTemplatesQuery query, CancellationToken cancellationToken)
    {
        var upstream = await upstreamClient.SendAsync<TemplateApiResource[]>(
            new GetTemplatesQuery(),
            cancellationToken);

        if (upstream is null)
        {
            return [];
        }

        return upstream
            .Select(static t => new GatewayTemplateResource
            {
                Id = t.Id,
                Name = t.Name,
                IsArchived = t.IsArchived,
            })
            .ToArray();
    }
}

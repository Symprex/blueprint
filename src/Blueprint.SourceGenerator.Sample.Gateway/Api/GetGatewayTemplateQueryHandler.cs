namespace Blueprint.SourceGenerator.Sample.Gateway.Api;

/// <summary>
/// Gateway handler that fetches a single template from the upstream service by identifier.
/// Demonstrates parameterized route placeholder substitution in the HTTP client — the
/// <see cref="GetTemplateQuery.Id"/> property is automatically URL-encoded and substituted
/// into the <c>/templates/{Id}</c> route template by the source-generated URL builder.
/// </summary>
public class GetGatewayTemplateQueryHandler(BlueprintHttpClient<SampleBlueprintApp> upstreamClient)
{
    /// <summary>
    /// Sends <see cref="GetTemplateQuery"/> to the upstream service and returns the mapped result,
    /// or <see cref="NotFoundResult{T}"/> when the upstream returns 404.
    /// </summary>
    /// <param name="query">The gateway query containing the template identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An OK result with the mapped template, or a not-found result.</returns>
    public async Task<Results<OkResult<GatewayTemplateResource>, NotFoundResult<GatewayTemplateResource>>> Handle(
        GetGatewayTemplateQuery query,
        CancellationToken cancellationToken)
    {
        try
        {
            var upstream = await upstreamClient.SendAsync<TemplateApiResource>(
                new GetTemplateQuery { Id = query.Id },
                cancellationToken);

            if (upstream is null)
            {
                return new NotFoundResult<GatewayTemplateResource>(
                    new GatewayTemplateResource { Id = query.Id, Name = string.Empty });
            }

            return new OkResult<GatewayTemplateResource>(new GatewayTemplateResource
            {
                Id = upstream.Id,
                Name = upstream.Name,
                IsArchived = upstream.IsArchived,
            });
        }
        catch (BlueprintApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new NotFoundResult<GatewayTemplateResource>(
                new GatewayTemplateResource { Id = query.Id, Name = string.Empty });
        }
    }
}

namespace Blueprint.SourceGenerator.Sample.Gateway.Api;

/// <summary>
/// Gateway handler that fetches a single template from the upstream service by identifier.
/// Demonstrates <see cref="BlueprintHttpClient.ForwardAsync{TInner, TResult}(ApiOperationContext, TInner, CallOptions, CancellationToken)"/> from a hand-written handler: the inner
/// <see cref="GetTemplateQuery"/> is sent through its own generated request builders, and the upstream's
/// <see cref="NotFoundResult{T}"/> comes back as that member of the declared result rather than as an exception.
/// </summary>
public class GetGatewayTemplateQueryHandler(BlueprintHttpClient<SampleBlueprintApp> upstreamClient)
{
    /// <summary>
    /// Forwards a fresh <see cref="GetTemplateQuery"/> to the upstream service and maps the result to the gateway's own
    /// resource, or to <see cref="NotFoundResult{T}"/> when the upstream returns its not-found result.
    /// </summary>
    /// <param name="query">The gateway query containing the template identifier.</param>
    /// <param name="context">The gateway operation being served, whose caller's <c>Authorization</c> header the forward sends.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An OK result with the mapped template, or a not-found result.</returns>
    public async Task<Results<OkResult<GatewayTemplateResource>, NotFoundResult<GatewayTemplateResource>>> Handle(
        GetGatewayTemplateQuery query,
        ApiOperationContext context,
        CancellationToken cancellationToken)
    {
        var upstream = await upstreamClient.ForwardAsync<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
            context,
            new GetTemplateQuery { Id = query.Id },
            cancellationToken: cancellationToken);

        if (upstream.IsT2)
        {
            return new NotFoundResult<GatewayTemplateResource>(
                new GatewayTemplateResource { Id = query.Id, Name = string.Empty });
        }

        var template = upstream.AsT1.Content;

        return new OkResult<GatewayTemplateResource>(new GatewayTemplateResource
        {
            Id = template.Id,
            Name = template.Name,
            IsArchived = template.IsArchived,
        });
    }
}

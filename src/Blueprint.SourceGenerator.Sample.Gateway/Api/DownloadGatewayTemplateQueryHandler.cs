using Blueprint.Http;

namespace Blueprint.SourceGenerator.Sample.Gateway.Api;

/// <summary>
/// Gateway handler that streams a template's content from the upstream service. Declaring <see cref="ProxyResult"/> as
/// the forward's result makes
/// <see cref="BlueprintHttpClient.ForwardAsync{TInner, TResult}(ApiOperationContext, TInner, CallOptions, CancellationToken)"/>
/// return as soon as the upstream's headers arrive, and the gateway copies the body to its caller as it is read. Passing
/// the operation's context forwards the caller's credential, as every hand-written forward here does.
/// </summary>
public class DownloadGatewayTemplateQueryHandler(BlueprintHttpClient<SampleBlueprintApp> upstreamClient)
{
    /// <summary>
    /// Forwards a fresh <see cref="DownloadTemplateQuery"/> to the upstream service and passes its response through.
    /// </summary>
    /// <param name="query">The gateway query containing the template identifier.</param>
    /// <param name="context">The operation's context, whose caller's credential the forward sends.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The upstream response, which the result owns and disposes once written.</returns>
    public Task<ProxyResult> Handle(DownloadGatewayTemplateQuery query, ApiOperationContext context, CancellationToken cancellationToken)
    {
        return upstreamClient.ForwardAsync<DownloadTemplateQuery, ProxyResult>(
            context,
            new DownloadTemplateQuery { Id = query.Id },
            cancellationToken: cancellationToken);
    }
}

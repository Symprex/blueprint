using System.ComponentModel;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Gateway.Api;

/// <summary>
/// Gateway query that fetches a template from the upstream service as the gateway's own service identity rather than as
/// the caller. Demonstrates <see cref="UpstreamCredentialAttribute{TCredential}"/>: its forwards authenticate with the
/// credential <see cref="ServiceTokenCredentialStrategy"/> creates, in place of the caller's <c>Authorization</c> header.
/// </summary>
/// <remarks>
/// <para>
/// Declared in the gateway rather than its contract assembly because the attribute names the gateway's strategy type.
/// </para>
/// <para>
/// The upstream authorises the service identity, not the caller, so the gateway must check the caller's own read
/// permission itself: <c>[Resource("template", "read")]</c> refuses a caller without it before anything is forwarded.
/// </para>
/// </remarks>
[ApiOperations("GatewayTemplates")]
[RootLink("/service/templates/{Id}")]
[HttpGet]
[Resource("template", "read")]
[UpstreamCredential<ServiceTokenCredentialStrategy>]
[Description("Retrieve a single template from the upstream template service as the gateway's service identity.")]
public partial class GetServiceTemplateQuery : IQuery<Results<OkResult<GatewayTemplateResource>, NotFoundResult<GatewayTemplateResource>>>
{
    /// <summary>
    /// The identifier of the upstream template to retrieve.
    /// </summary>
    [Description("The unique identifier of the template.")]
    public required string Id { get; set; }
}

/// <summary>
/// Handles <see cref="GetServiceTemplateQuery"/> by forwarding a fresh <see cref="GetTemplateQuery"/> with the operation's
/// context, so the query's <see cref="UpstreamCredentialAttribute{TCredential}"/> chooses the credential.
/// </summary>
public class GetServiceTemplateQueryHandler(BlueprintHttpClient<SampleBlueprintApp> upstreamClient)
{
    /// <summary>
    /// Forwards the query and maps the upstream template to the gateway's resource.
    /// </summary>
    /// <param name="query">The gateway query.</param>
    /// <param name="context">The gateway operation being served.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An OK result with the mapped template, or a not-found result.</returns>
    public async Task<Results<OkResult<GatewayTemplateResource>, NotFoundResult<GatewayTemplateResource>>> Handle(
        GetServiceTemplateQuery query,
        ApiOperationContext context,
        CancellationToken cancellationToken)
    {
        var upstream = await upstreamClient.ForwardAsync<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
            context,
            new GetTemplateQuery { Id = query.Id },
            cancellationToken: cancellationToken);

        if (upstream.IsT2)
        {
            return new NotFoundResult<GatewayTemplateResource>(new GatewayTemplateResource { Id = query.Id, Name = string.Empty });
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

using System.ComponentModel;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Gateway.Api;

/// <summary>
/// Retrieves a single template from the upstream template service. Minimal mode: the inner <see cref="GetTemplateQuery"/>'s
/// properties, its result and a forwarding handler are generated, so the gateway declares only its route, method and
/// authorisation. The upstream's <see cref="NotFoundResult{T}"/> is answered as the gateway's own 404, and the resource
/// carries the gateway's links, never the upstream's.
/// </summary>
[Expose<GetTemplateQuery>]
[ApiOperations("GatewayTemplates")]
[MustBeAuthenticated]
[SelfLink(typeof(TemplateApiResource), "/templates/{Id}")]
[HttpGet]
[Description("Retrieve a single template from the upstream template service via the gateway.")]
public partial class GetGatewayTemplateQuery : IQuery<Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>;

using System.ComponentModel;
using System.Net;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Gateway.Api;

/// <summary>
/// Retrieves a template within the caller's tenant from the upstream template service. Minimal mode with a declared
/// credential and error translation (#69): the inner <c>TenantId</c> is bound from the caller's <c>tenant_id</c> claim by
/// the gateway's <c>[GatewayBind]</c> (#66), so the gateway operation has none of its own; the generated forward exchanges
/// the caller's token for one scoped to that bound tenant, which the upstream's tenant isolation requires, and answers the
/// upstream's <c>not_found</c> problem as the gateway's own.
/// </summary>
/// <remarks>
/// Declared in the gateway rather than its contract assembly because <see cref="ExchangeTenantTokenAttribute"/> names the
/// gateway's strategy.
/// </remarks>
[Expose<GetTenantTemplateQuery>]
[ApiOperations("GatewayTemplates")]
[MustBeAuthenticated]
[RootLink("/tenants/current/templates/{Id}")]
[HttpGet]
[ExchangeTenantToken(TenantIdProperty = nameof(GetTenantTemplateQuery.TenantId))]
[TranslateError("not_found", nameof(TenantTemplateMissing))]
[Description("Retrieve a single template within a tenant from the upstream template service via the gateway.")]
public partial class GetGatewayTenantTemplateQuery : IQuery<TemplateApiResource>
{
    /// <summary>
    /// The gateway's answer to the upstream's <c>not_found</c>.
    /// </summary>
    public static readonly ApiExceptionFactory TenantTemplateMissing = new("Tenant template missing", "gateway_tenant_template_missing", HttpStatusCode.NotFound);
}

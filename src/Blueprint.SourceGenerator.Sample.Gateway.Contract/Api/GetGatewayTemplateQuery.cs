using System.ComponentModel;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample.Gateway.Contract.Api;

/// <summary>
/// Gateway query that fetches a single template from the upstream template service by identifier.
/// Demonstrates the HTTP client's URL placeholder substitution for parameterized routes.
/// </summary>
[ApiOperations("GatewayTemplates")]
[SelfLink(typeof(GatewayTemplateResource), "/gateway/templates/{Id}")]
[HttpGet]
[Description("Retrieve a single template from the upstream template service via the gateway.")]
public partial class GetGatewayTemplateQuery : IQuery<Results<OkResult<GatewayTemplateResource>, NotFoundResult<GatewayTemplateResource>>>
{
    /// <summary>
    /// The identifier of the upstream template to retrieve.
    /// </summary>
    [Description("The unique identifier of the template.")]
    public required string Id { get; set; }
}

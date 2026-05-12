using System.ComponentModel;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample.Gateway.Contract.Api;

/// <summary>
/// Gateway query that lists templates from the upstream template service.
/// Demonstrates a gateway pattern where the operation itself is simple but the handler
/// delegates to a <c>BlueprintHttpClient</c> to reach the upstream service.
/// </summary>
[ApiOperations("GatewayTemplates")]
[Link(typeof(GatewayTemplateResource), "/gateway/templates")]
[HttpGet]
[Description("List all templates from the upstream template service via the gateway.")]
public partial class ListGatewayTemplatesQuery : IQuery<OkResult<GatewayTemplateResource[]>>;

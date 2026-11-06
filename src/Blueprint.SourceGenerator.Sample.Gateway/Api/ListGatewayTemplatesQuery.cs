using System.ComponentModel;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Gateway.Api;

/// <summary>
/// Lists templates from the upstream template service. Minimal mode: <see cref="GetTemplatesQuery"/> has no properties, so
/// only its result and a forwarding handler are generated.
/// </summary>
[Expose<GetTemplatesQuery>]
[ApiOperations("GatewayTemplates")]
[MustBeAuthenticated]
[RootLink("/templates")]
[HttpGet]
[Description("List all templates from the upstream template service via the gateway.")]
public partial class ListGatewayTemplatesQuery : IQuery<TemplateApiResource[]>;

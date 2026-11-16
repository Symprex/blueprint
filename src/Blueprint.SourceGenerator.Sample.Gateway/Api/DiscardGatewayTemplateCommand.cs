using System.ComponentModel;
using Blueprint.Http;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Gateway.Api;

/// <summary>
/// Discards a template through the upstream template service. Minimal mode over an inner command that answers with a
/// legacy deletion carrying no data: the gateway answers with the gateway's own href for the template, from its
/// <see cref="GetGatewayTemplateQuery"/> self link, as a hand-written handler returning its own
/// <see cref="ResourceDeleted{TResource}"/> would (Symprex/blueprint#188).
/// </summary>
[Expose<DiscardTemplateCommand>]
[ApiOperations("GatewayTemplates")]
[MustBeAuthenticated]
[Link(typeof(TemplateApiResource), "/templates/{Id}/discard", Rel = "discard")]
[HttpDelete]
[Description("Discard a template through the gateway.")]
public partial class DiscardGatewayTemplateCommand : ICommand<ResourceEvent<TemplateApiResource>>;

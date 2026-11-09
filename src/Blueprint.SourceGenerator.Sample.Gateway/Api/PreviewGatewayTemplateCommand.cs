using System.ComponentModel;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Gateway.Api;

/// <summary>
/// Previews a template in the upstream template service. Minimal mode with a JSON body: the inner
/// <see cref="PreviewTemplateCommand"/>'s <c>MergeFields</c> dictionary is a generated property, read from the request body
/// by Blueprint's own generated population rather than through <c>GatewayBlueprintAppJsonContext</c>, whose
/// source-generated metadata cannot see a property another generator emitted.
/// </summary>
[Expose<PreviewTemplateCommand>]
[ApiOperations("GatewayTemplates")]
[MustBeAuthenticated]
[RootLink("/templates/{Id}/preview")]
[HttpPost]
[Description("Preview a rendered template in the upstream template service via the gateway.")]
public partial class PreviewGatewayTemplateCommand : ICommand<TemplatePreviewApiResource>;

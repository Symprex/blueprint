using System.ComponentModel;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Gateway.Api;

/// <summary>
/// Imports a template's source into the upstream template service. Minimal mode with a stream body (#71): the inner
/// <see cref="ImportTemplateSourceCommand"/>'s <c>[FromBody] Stream</c> is handed this request's body, and the generated
/// forward sends that body upstream as it arrives, never buffering it.
/// </summary>
[Expose<ImportTemplateSourceCommand>]
[ApiOperations("GatewayTemplates")]
[MustBeAuthenticated]
[RootLink("/templates/{Id}/source")]
[HttpPut]
[Description("Import a template's source into the upstream template service via the gateway.")]
public partial class ImportGatewayTemplateSourceCommand : ICommand<TemplateUploadApiResource>;

using System.ComponentModel;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Gateway.Api;

/// <summary>
/// Attaches a file to a template in the upstream template service. Minimal mode with a multipart body (#71): the inner
/// <see cref="AttachTemplateFileCommand"/>'s <c>IFormFile</c> makes this operation's body <c>multipart/form-data</c> too, and
/// the generated forward sends the inbound file upstream as a part of its own, streamed.
/// </summary>
[Expose<AttachTemplateFileCommand>]
[ApiOperations("GatewayTemplates")]
[MustBeAuthenticated]
[RootLink("/templates/{Id}/attachments")]
[HttpPost]
[Description("Attach a file to a template in the upstream template service via the gateway.")]
public partial class AttachGatewayTemplateFileCommand : ICommand<TemplateUploadApiResource>;

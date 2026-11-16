using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Imports a template's source from the raw request body. Demonstrates stream binding (#71): the <c>[FromBody]</c>
/// <see cref="Stream"/> is handed the request body unread, so the handler reads it as it arrives, and a client or a gateway
/// forward sends it as a stream, never buffered.
/// </summary>
[ApiOperations("Templates")]
[RootLink("/templates/{Id}/source")]
[HttpPut]
[Resource("template", "write")]
[Description("Import a template's source from the request body.")]
public partial class ImportTemplateSourceCommand : ICommand<TemplateUploadApiResource>
{
    /// <summary>
    /// The identifier of the template to import into (bound from the route).
    /// </summary>
    [Required]
    [Description("The unique identifier of the template.")]
    public string Id { get; set; } = null!;

    /// <summary>
    /// The source, the whole request body.
    /// </summary>
    [FromBody]
    [Description("The template source.")]
    public Stream Content { get; set; } = Stream.Null;
}

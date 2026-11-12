using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Microsoft.AspNetCore.Http;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Attaches a file to a template. Demonstrates multipart binding (#71): the <see cref="IFormFile"/> property makes the request
/// body <c>multipart/form-data</c>, its file read by name and <see cref="Description"/> from a form field, and a client or a
/// gateway forward sends it as one, streaming the file.
/// </summary>
[ApiOperations("Templates")]
[RootLink("/templates/{Id}/attachments")]
[HttpPost]
[Resource("template", "write")]
[Description("Attach a file to a template.")]
public partial class AttachTemplateFileCommand : ICommand<TemplateUploadApiResource>
{
    /// <summary>
    /// The identifier of the template to attach the file to (bound from the route).
    /// </summary>
    [Required]
    [Description("The unique identifier of the template.")]
    public string Id { get; set; } = null!;

    /// <summary>
    /// What the file is, from the form field <c>description</c>.
    /// </summary>
    [Description("What the attached file is.")]
    public string? Description { get; set; }

    /// <summary>
    /// The file, from the form's <c>file</c> part.
    /// </summary>
    [Required]
    [Description("The file to attach.")]
    public IFormFile File { get; set; } = null!;
}

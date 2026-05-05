using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Clones an existing template with a new name. Demonstrates a POST action that takes
/// both route parameters and a request body, producing a resource-event result (template.cloned).
/// </summary>
[ApiOperations("Templates")]
[RootLink("/templates/{Id}/clone")]
[HttpPost]
[FromResource(typeof(CreateTemplateCommand))]
[Description("Clone a template with a new name.")]
public partial class CloneTemplateCommand : ICommand<ResourceEventResult<TemplateApiResource>>
{
    /// <summary>
    /// The identifier of the source template to clone.
    /// </summary>
    [Description("The unique identifier of the template to clone.")]
    public required string Id { get; set; }

    /// <summary>
    /// The name for the new cloned template. Required and limited to 200 characters.
    /// </summary>
    [Required]
    [MaxLength(200)]
    [Description("The display name for the new cloned template.")]
    public string? NewName { get; set; }
}

using System.ComponentModel.DataAnnotations;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Persistence;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Renders a template with sample data and returns the rendered output for preview purposes.
/// Demonstrates body-to-result transformation without persisting changes.
/// </summary>
[HttpMethod("POST")]
[RootLink("/templates/{Id}/preview")]
[ApiOperations("Templates")]
[Resource("template", "read")]
[DoNotSaveChanges]
public partial class PreviewTemplateCommand : ICommand<TemplatePreviewApiResource>
{
    /// <summary>
    /// The template identifier to render.
    /// </summary>
    [Required]
    public string Id { get; set; } = null!;

    /// <summary>
    /// A dictionary of merge-field names to values for the template rendering.
    /// </summary>
    [Required]
    public Dictionary<string, string> MergeFields { get; set; } = new();
}

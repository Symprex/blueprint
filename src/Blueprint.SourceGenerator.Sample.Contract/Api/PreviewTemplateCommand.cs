using System.ComponentModel;
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
[Description("Preview a rendered template with sample merge data.")]
public partial class PreviewTemplateCommand : ICommand<TemplatePreviewApiResource>
{
    /// <summary>
    /// The template identifier to render.
    /// </summary>
    [Required]
    [Description("The unique identifier of the template to preview.")]
    public string Id { get; set; } = null!;

    /// <summary>
    /// A dictionary of merge-field names to values for the template rendering.
    /// </summary>
    [Required]
    [Description("Merge-field name/value pairs for template rendering.")]
    public Dictionary<string, string> MergeFields { get; set; } = new();

    /// <summary>
    /// An optional recipient to render the preview for, whose values replace <c>{{Recipient.Name}}</c> and
    /// <c>{{Recipient.Email}}</c>. A nested object the sample app's serializer context lists, so its OpenAPI schema
    /// describes its fields, but the gateway's does not, so a gateway exposing this command reads it with the reflection
    /// fallback of its JSON options.
    /// </summary>
    [Description("The recipient to render the preview for.")]
    public TemplatePreviewRecipient? Recipient { get; set; }
}

/// <summary>
/// The recipient a template preview is rendered for.
/// </summary>
public class TemplatePreviewRecipient
{
    /// <summary>
    /// The recipient's display name.
    /// </summary>
    [Description("The recipient's display name.")]
    public string? Name { get; set; }

    /// <summary>
    /// The recipient's email address.
    /// </summary>
    [Description("The recipient's email address.")]
    public string? Email { get; set; }
}

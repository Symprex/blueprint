using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Updates an existing template's metadata and content. Demonstrates a URL-parameterized command
/// with body binding at the same route pattern as the GET query but with a different HTTP method.
/// Returns a resource-event result (template.updated).
/// Also demonstrates <see cref="IValidatableObject"/> for cross-field validation: at least one
/// content source (HtmlSource or PlainTextSource) must be provided.
/// </summary>
[ApiOperations("Templates")]
[RootLink("/templates/{Id}")]
[HttpPut]
[FromResource(typeof(CreateTemplateCommand))]
[Description("Update an existing template's name and content.")]
public partial class UpdateTemplateCommand : ICommand<ResourceEventResult<TemplateApiResource>>, IValidatableObject
{
    /// <summary>
    /// The identifier of the template to update (bound from the route).
    /// </summary>
    [Description("The unique identifier of the template to update.")]
    public required string Id { get; set; }

    /// <summary>
    /// The updated name for the template. Required and limited to 200 characters.
    /// </summary>
    [Required]
    [MaxLength(200)]
    [Description("The new display name for the template.")]
    public string? Name { get; set; }

    /// <summary>
    /// The updated HTML source content of the template.
    /// </summary>
    [MaxLength(50000)]
    [Description("The updated HTML source content.")]
    public string? HtmlSource { get; set; }

    /// <summary>
    /// The updated plain text source content of the template.
    /// </summary>
    [MaxLength(10000)]
    [Description("The updated plain text content.")]
    public string? PlainTextSource { get; set; }

    /// <summary>
    /// Validates cross-field constraints: at least one content source must be provided.
    /// </summary>
    /// <param name="validationContext">The validation context provided by the framework.</param>
    /// <returns>Validation errors when neither HtmlSource nor PlainTextSource is supplied.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(this.HtmlSource) && string.IsNullOrWhiteSpace(this.PlainTextSource))
        {
            yield return new ValidationResult(
                "At least one content source (HtmlSource or PlainTextSource) must be provided.",
                [nameof(this.HtmlSource), nameof(this.PlainTextSource)]);
        }
    }
}

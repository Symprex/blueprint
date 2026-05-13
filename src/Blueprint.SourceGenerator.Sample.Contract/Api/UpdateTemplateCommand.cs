using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net;
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
/// <remarks>
/// <para>
/// Demonstrates <see cref="FromResourceAttribute"/> inheriting the parent
/// <see cref="CreateTemplateCommand"/>'s <c>[Resource("template", "write")]</c> scope pair so the same
/// resource authorization rule applies to updates without restating the scope metadata.
/// </para>
/// <para>
/// Also demonstrates <see cref="ApiExceptionFactory"/> fields for compile-time OpenAPI error
/// response discovery: the <c>NotFound</c> and <c>Conflict</c> factories are scanned by
/// <c>ApiExceptionFactoryModule</c> and projected as additional 404 and 409 response entries
/// in the generated descriptor metadata.
/// </para>
/// </remarks>
[ApiOperations("Templates")]
[RootLink("/templates/{Id}")]
[HttpPut]
[FromResource(typeof(CreateTemplateCommand))]
[Description("Update an existing template's name and content.")]
public partial class UpdateTemplateCommand : ICommand<ResourceEventResult<TemplateApiResource>>, IValidatableObject
{
    /// <summary>
    /// Thrown when the template does not exist. Scanned at compile time by
    /// <c>ApiExceptionFactoryModule</c> to produce a 404 OpenAPI response entry.
    /// </summary>
    public static readonly ApiExceptionFactory NotFound = new(
        "Template not found",
        "template_not_found",
        HttpStatusCode.NotFound);

    /// <summary>
    /// Thrown when the template has been modified concurrently. Scanned at compile time by
    /// <c>ApiExceptionFactoryModule</c> to produce a 409 OpenAPI response entry.
    /// </summary>
    public static readonly ApiExceptionFactory Conflict = new(
        "Template version conflict",
        "template_conflict",
        HttpStatusCode.Conflict);

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

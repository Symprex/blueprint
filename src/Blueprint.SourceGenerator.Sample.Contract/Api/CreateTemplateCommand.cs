using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Blueprint.Auditing;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Creates a new template in the system. Demonstrates the generated validation pipeline
/// by requiring a name and enforcing length constraints on content fields.
/// Returns a resource-event result (201 Created with event envelope).
/// </summary>
[ApiOperations("Templates")]
[RootLink("/templates")]
[HttpPost]
[Resource("template", "write")]
[Description("Create a new template with name and optional content.")]
public partial class CreateTemplateCommand : ICommand<ResourceEventResult<TemplateApiResource>>
{
    /// <summary>
    /// The name of the template to create. Required and limited to 200 characters.
    /// </summary>
    [Required]
    [MaxLength(200)]
    [Description("The display name for the template.")]
    public string? Name { get; set; }

    /// <summary>
    /// An optional URL-safe slug for the template (lowercase letters, digits, and hyphens only).
    /// Demonstrates compile-time <see cref="RegularExpressionAttribute"/> fast-path validation
    /// and OpenAPI <c>pattern</c> constraint projection.
    /// </summary>
    [RegularExpression(@"^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    [MaxLength(100)]
    [Description("URL-safe slug for the template (lowercase letters, digits, hyphens).")]
    public string? Slug { get; set; }

    /// <summary>
    /// Optional priority for template ordering. Demonstrates compile-time
    /// <see cref="RangeAttribute"/> fast-path validation and OpenAPI min/max constraints.
    /// </summary>
    [Range(1, 1000)]
    [Description("Priority for template ordering (1-1000).")]
    public int Priority { get; set; } = 100;

    /// <summary>
    /// The HTML source content of the template. Limited to 50000 characters.
    /// Marked [Sensitive] to signal audit sinks that template content may contain embedded PII.
    /// </summary>
    [MaxLength(50000)]
    [Sensitive]
    [Description("The HTML source content of the template.")]
    public string? HtmlSource { get; set; }

    /// <summary>
    /// The plain text source content of the template. Limited to 10000 characters.
    /// Marked [Sensitive] to signal audit sinks that template content may contain embedded PII.
    /// </summary>
    [MaxLength(10000)]
    [Sensitive]
    [Description("The plain text fallback content of the template.")]
    public string? PlainTextSource { get; set; }

    /// <summary>
    /// Optional author email address. Demonstrates <see cref="EmailAddressAttribute"/>
    /// fast-path validation and OpenAPI <c>format: "email"</c> projection.
    /// </summary>
    [EmailAddress]
    [Description("Email address of the template author.")]
    public string? AuthorEmail { get; set; }

    /// <summary>
    /// Optional callback URL for template lifecycle events. Demonstrates <see cref="UrlAttribute"/>
    /// fast-path validation and OpenAPI <c>format: "uri"</c> projection.
    /// </summary>
    [Url]
    [Description("Callback URL for template lifecycle events.")]
    public string? CallbackUrl { get; set; }
}

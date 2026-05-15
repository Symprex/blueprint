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
/// <remarks>
/// Demonstrates <see cref="ResourceAttribute"/>-based authorization: the generated descriptor carries the
/// <c>("template", "write")</c> scope pair so a registered <see cref="Blueprint.Authorization.IResourceAuthorizationProvider"/>
/// can decide whether the current user may create templates.
/// </remarks>
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
    [OpenApiExample("Welcome Email")]
    public string? Name { get; set; }

    /// <summary>
    /// An optional URL-safe slug for the template (lowercase letters, digits, and hyphens only).
    /// Demonstrates compile-time <see cref="RegularExpressionAttribute"/> fast-path validation
    /// and OpenAPI <c>pattern</c> constraint projection, plus <see cref="DeniedValuesAttribute"/>
    /// to block reserved slug values at build time.
    /// </summary>
    [RegularExpression(@"^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    [MaxLength(100)]
    [DeniedValues("admin", "api", "system", "health")]
    [Description("URL-safe slug for the template (lowercase letters, digits, hyphens). Reserved values: admin, api, system, health.")]
    public string? Slug { get; set; }

    /// <summary>
    /// Visibility level for the template. Demonstrates <see cref="AllowedValuesAttribute"/>
    /// (.NET 8) fast-path validation: the generator emits inline inequality checks at build time
    /// instead of falling back to runtime validation.
    /// </summary>
    [AllowedValues("public", "internal", "private")]
    [Description("Visibility level: public, internal, or private.")]
    public string? Visibility { get; set; }

    /// <summary>
    /// Optional priority for template ordering. Demonstrates compile-time
    /// <see cref="RangeAttribute"/> fast-path validation and OpenAPI min/max constraints.
    /// </summary>
    [Range(1, 1000)]
    [DefaultValue(100)]
    [OpenApiExample(42)]
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

    /// <summary>
    /// The default sort order preference for search results within this template's collection.
    /// Demonstrates enum-typed request body properties rendered as <c>oneOf: [{ $ref }]</c>
    /// referencing a top-level enum component schema.
    /// </summary>
    [Description("Default sort order preference for this template.")]
    public TemplateSortOrder? DefaultSortOrder { get; set; }

    /// <summary>
    /// Legacy template category. Superseded by Tags.
    /// Demonstrates property-level <c>deprecated: true</c> in OpenAPI schema.
    /// </summary>
    [Obsolete("Use Tags instead.")]
    [Description("Legacy category — use Tags instead.")]
    public string? LegacyCategory { get; set; }

    /// <summary>
    /// Freeform tags for categorizing the template. Demonstrates OpenAPI array rendering
    /// with inline <c>items</c> schema for primitive element types.
    /// </summary>
    [Description("Freeform tags for categorizing the template.")]
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Optional merge field definitions for the template. Demonstrates OpenAPI array rendering
    /// with <c>$ref</c> for complex element types — each item references the
    /// <c>TemplateMergeFieldDto</c> component schema instead of inlining properties.
    /// </summary>
    [Description("Merge field definitions available in the template.")]
    public List<TemplateMergeFieldDto>? MergeFields { get; set; }
}

/// <summary>
/// A merge field definition for a template. Used to demonstrate OpenAPI <c>$ref</c>
/// rendering for complex items inside a request body array property.
/// </summary>
public class TemplateMergeFieldDto
{
    /// <summary>
    /// The placeholder key used in the template source (e.g. "FirstName").
    /// </summary>
    [Required]
    [MaxLength(100)]
    [Description("The placeholder key used in the template source.")]
    public string? Key { get; set; }

    /// <summary>
    /// An optional default value to use when the merge field is not supplied at render time.
    /// </summary>
    [MaxLength(500)]
    [Description("Default value when the merge field is not supplied.")]
    public string? DefaultValue { get; set; }
}

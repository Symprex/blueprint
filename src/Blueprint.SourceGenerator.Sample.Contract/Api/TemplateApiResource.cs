using System.ComponentModel;
using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Represents a single template.
/// </summary>
[DisplayName("Email Template")]
public partial class TemplateApiResource : ApiResource
{
    [System.Text.Json.Serialization.JsonConstructor]
    private TemplateApiResource()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TemplateApiResource"/> class.
    /// </summary>
    /// <param name="id">The id of this template.</param>
    /// <param name="name">The display name of this template.</param>
    /// <param name="htmlSource">The HTML source of this template</param>
    /// <param name="plainTextSource">The plain text source of this template.</param>
    /// <param name="ownerId">The identifier of the template owner.</param>
    public TemplateApiResource(string id, string name, string htmlSource, string plainTextSource, string ownerId = "owner-1")
    {
        this.Id = id;
        this.Name = name;
        this.HtmlSource = htmlSource;
        this.PlainTextSource = plainTextSource;
        this.Owner = new Expandable<OwnerApiResource>(ownerId);
    }

    /// <summary>
    /// The id of this template.
    /// </summary>
    [Description("The unique identifier for this template.")]
    [OpenApiExample("tmpl-abc-123")]
    public string Id { get; init; } = null!;

    /// <summary>
    /// The display name of this template.
    /// </summary>
    [Description("The human-readable display name of this template.")]
    public string Name { get; init; } = null!;

    /// <summary>
    /// The HTML source of this template.
    /// </summary>
    [Description("The full HTML source content of this template.")]
    public string HtmlSource { get; init; } = null!;

    /// <summary>
    /// The plain text source of this template.
    /// </summary>
    [Description("The plain text fallback content of this template.")]
    public string PlainTextSource { get; init; } = null!;

    /// <summary>
    /// Gets or sets a value indicating whether the template is archived (soft-deleted).
    /// </summary>
    [Description("Whether the template has been archived (soft-deleted).")]
    public bool IsArchived { get; set; }

    /// <summary>
    /// The owner of this template. Expandable via <c>?expand=Owner</c> query parameter,
    /// which causes the generated pipeline to resolve an <c>IResourceExpander</c> from DI
    /// and populate <see cref="Expandable{T}.Value"/> with the full <see cref="OwnerApiResource"/>.
    /// </summary>
    [Description("The owner of this template, expandable via ?expand=Owner.")]
    public Expandable<OwnerApiResource>? Owner { get; set; }
}

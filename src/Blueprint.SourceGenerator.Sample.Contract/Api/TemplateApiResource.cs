using System.ComponentModel;
using Blueprint.Model;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Represents a single template.
/// </summary>
public partial class TemplateApiResource : ApiResource
{
    // For JSON
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
    public string Id { get; private set; } = null!;

    /// <summary>
    /// The display name of this template.
    /// </summary>
    [Description("The human-readable display name of this template.")]
    public string Name { get; private set; } = null!;

    /// <summary>
    /// The HTML source of this template.
    /// </summary>
    [Description("The full HTML source content of this template.")]
    public string HtmlSource { get; private set; } = null!;

    /// <summary>
    /// The plain text source of this template.
    /// </summary>
    [Description("The plain text fallback content of this template.")]
    public string PlainTextSource { get; private set; } = null!;

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

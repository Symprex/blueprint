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
    public TemplateApiResource(string id, string name, string htmlSource, string plainTextSource)
    {
        this.Id = id;
        this.Name = name;
        this.HtmlSource = htmlSource;
        this.PlainTextSource = plainTextSource;
    }

    /// <summary>
    /// The id of this template.
    /// </summary>
    public string Id { get; private set; } = null!;

    /// <summary>
    /// The display name of this template.
    /// </summary>
    public string Name { get; private set; } = null!;

    /// <summary>
    /// The HTML source of this template.
    /// </summary>
    public string HtmlSource { get; private set; } = null!;

    /// <summary>
    /// The plain text source of this template.
    /// </summary>
    public string PlainTextSource { get; private set; } = null!;

    /// <summary>
    /// Gets or sets a value indicating whether the template is archived (soft-deleted).
    /// </summary>
    public bool IsArchived { get; set; }
}

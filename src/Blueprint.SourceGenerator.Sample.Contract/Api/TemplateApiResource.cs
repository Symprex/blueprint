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
    /// <param name="htmlSource">The HTML source of this template</param>
    /// <param name="plainTextSource">The plain text source of this template.</param>
    public TemplateApiResource(string id, string htmlSource, string plainTextSource)
    {
        this.Id = id;
        this.HtmlSource = htmlSource;
        this.PlainTextSource = plainTextSource;
    }

    /// <summary>
    /// The id of this template
    /// </summary>
    public string Id { get; private set; } = null!;

    /// <summary>
    /// The HTML source of this template.
    /// </summary>
    public string HtmlSource { get; private set; } = null!;

    /// <summary>
    /// The plain text source of this template.
    /// </summary>
    public string PlainTextSource { get; private set; } = null!;
}

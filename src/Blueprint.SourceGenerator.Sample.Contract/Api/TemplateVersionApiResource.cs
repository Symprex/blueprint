using Blueprint.Model;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Represents a version/revision entry for a template.
/// </summary>
public partial class TemplateVersionApiResource : ApiResource
{
    // For JSON
    private TemplateVersionApiResource()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TemplateVersionApiResource"/> class.
    /// </summary>
    /// <param name="version">The version number.</param>
    /// <param name="publishedAt">When this version was published.</param>
    /// <param name="templateId">The parent template identifier.</param>
    public TemplateVersionApiResource(int version, DateTimeOffset publishedAt, string templateId)
    {
        this.Version = version;
        this.PublishedAt = publishedAt;
        this.TemplateId = templateId;
    }

    /// <summary>
    /// The version number (monotonically increasing per template).
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// When this version was published.
    /// </summary>
    public DateTimeOffset PublishedAt { get; private set; }

    /// <summary>
    /// The parent template identifier.
    /// </summary>
    public string TemplateId { get; private set; } = null!;
}

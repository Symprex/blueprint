using Blueprint.Model;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Represents the rendered preview output of a template with merge fields applied.
/// </summary>
public partial class TemplatePreviewApiResource : ApiResource
{
    // For JSON deserialization
    private TemplatePreviewApiResource()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TemplatePreviewApiResource"/> class.
    /// </summary>
    /// <param name="templateId">The source template identifier.</param>
    /// <param name="renderedHtml">The rendered HTML output.</param>
    /// <param name="renderedPlainText">The rendered plain text output.</param>
    public TemplatePreviewApiResource(string templateId, string renderedHtml, string renderedPlainText)
    {
        this.TemplateId = templateId;
        this.RenderedHtml = renderedHtml;
        this.RenderedPlainText = renderedPlainText;
    }

    /// <summary>
    /// The source template identifier.
    /// </summary>
    public string TemplateId { get; private set; } = null!;

    /// <summary>
    /// The rendered HTML output with merge fields applied.
    /// </summary>
    public string RenderedHtml { get; private set; } = null!;

    /// <summary>
    /// The rendered plain text output with merge fields applied.
    /// </summary>
    public string RenderedPlainText { get; private set; } = null!;
}

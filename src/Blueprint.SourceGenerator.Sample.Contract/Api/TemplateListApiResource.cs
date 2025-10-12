using Blueprint.Model;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Represents a list of templates.
/// </summary>
public partial class TemplateListApiResource : ApiResource
{
    // For JSON
    private TemplateListApiResource()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TemplateApiResource"/> class.
    /// </summary>
    /// <param name="id">The id of this template.</param>
    /// <param name="htmlSource">The HTML source of this template</param>
    /// <param name="plainTextSource">The plain text source of this template.</param>
    public TemplateListApiResource(TemplateApiResource[] templates)
    {
        this.Templates = templates;
    }

    /// <summary>
    /// The templates.
    /// </summary>
    public TemplateApiResource[] Templates { get; }
}

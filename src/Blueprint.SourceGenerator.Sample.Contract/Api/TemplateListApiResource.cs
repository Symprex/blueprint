using Blueprint.Model;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Represents a list of templates.
/// </summary>
public partial class TemplateListApiResource : ApiResource
{
    // For JSON
    private TemplateListApiResource()
        : this([])
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TemplateListApiResource"/> class.
    /// </summary>
    /// <param name="templates">The templates included in the resource.</param>
    public TemplateListApiResource(TemplateApiResource[] templates)
    {
        this.Templates = templates;
    }

    /// <summary>
    /// The templates.
    /// </summary>
    public TemplateApiResource[] Templates { get; }
}

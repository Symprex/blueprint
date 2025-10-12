using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Operation handlers for template related operations.
/// </summary>
/// <seealso cref="GetTemplatesQuery"/>
[MustBeAuthenticated]
[SelfLink(typeof(TemplateApiResource), "/templates/{Id}")]
[HttpGet]
public partial class GetTemplateInlineQuery : IQuery<TemplateApiResource>
{
    public required string Id { get; init; }

    /// <summary>
    /// Handles the <see cref="GetTemplateInlineQuery" /> operation.
    /// </summary>
    /// <returns>All available templates.</returns>
    public TemplateApiResource Handle()
    {
        throw new NotImplementedException($"Cannot get template with id {this.Id}");
    }
}

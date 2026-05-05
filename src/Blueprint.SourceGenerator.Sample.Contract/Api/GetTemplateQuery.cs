using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Gets a single template by its identifier. Demonstrates self-link generation for a parameterized
/// route that targets the same resource as the list query.
/// </summary>
[ApiOperations("Templates")]
[MustBeAuthenticated]
[SelfLink(typeof(TemplateApiResource), "/templates/{Id}")]
[HttpGet]
public partial class GetTemplateQuery : IQuery<TemplateApiResource>
{
    /// <summary>
    /// The identifier of the template to retrieve.
    /// </summary>
    public required string Id { get; set; }
}

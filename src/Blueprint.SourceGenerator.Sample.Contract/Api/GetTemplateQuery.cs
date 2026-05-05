using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Gets a single template by its identifier. Demonstrates a discriminated union result contract
/// (<c>Results&lt;OkResult&lt;T&gt;, NotFoundResult&lt;T&gt;&gt;</c>) that maps to distinct HTTP status codes
/// (200 OK vs 404 Not Found) through the generated pipeline, with both outcomes visible in OpenAPI.
/// Also demonstrates self-link generation for a parameterized route.
/// </summary>
[ApiOperations("Templates")]
[FromResource(typeof(GetTemplatesQuery))]
[SelfLink(typeof(TemplateApiResource), "/templates/{Id}")]
[HttpGet]
public partial class GetTemplateQuery : IQuery<Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>
{
    /// <summary>
    /// The identifier of the template to retrieve.
    /// </summary>
    public required string Id { get; set; }
}

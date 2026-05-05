using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Publishes a template, making it available for use. Demonstrates a discriminated union result
/// with <c>ConflictResult&lt;T&gt;</c> (409 Conflict) when the template is already published,
/// alongside the success path (<c>ResourceEventResult&lt;T&gt;</c>, 201 Created).
/// </summary>
[ApiOperations("Templates")]
[RootLink("/templates/{Id}/publish")]
[HttpPost]
[FromResource(typeof(CreateTemplateCommand))]
public partial class PublishTemplateCommand : ICommand<Results<ResourceEventResult<TemplateApiResource>, ConflictResult<TemplateApiResource>>>
{
    /// <summary>
    /// The identifier of the template to publish.
    /// </summary>
    public required string Id { get; set; }
}

using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Publishes a template, making it available for use. Demonstrates an action-style command
/// at a nested route that produces a resource-event result (template.published).
/// </summary>
[ApiOperations("Templates")]
[RootLink("/templates/{Id}/publish")]
[HttpPost]
[FromResource(typeof(CreateTemplateCommand))]
public partial class PublishTemplateCommand : ICommand<ResourceEventResult<TemplateApiResource>>
{
    /// <summary>
    /// The identifier of the template to publish.
    /// </summary>
    public required string Id { get; set; }
}

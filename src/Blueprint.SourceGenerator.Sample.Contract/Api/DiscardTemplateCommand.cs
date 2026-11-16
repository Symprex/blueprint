using System.ComponentModel;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Discards a template, answering with a legacy <see cref="ResourceDeleted{TResource}"/> made from the template's self
/// query: the service writes it with its own <c>href</c> and no data, as Signature365's services write a deletion. It needs
/// <c>("template", "write")</c>, as <see cref="ArchiveTemplateCommand"/> does. The sample gateway exposes it, and answers with
/// the gateway's own href for the template (Symprex/blueprint#188).
/// </summary>
[ApiOperations("Templates")]
[Link(typeof(TemplateApiResource), "/templates/{Id}/discard", Rel = "discard")]
[HttpDelete]
[Resource("template", "write")]
[Description("Discard a template.")]
public partial class DiscardTemplateCommand : ICommand<ResourceEvent<TemplateApiResource>>
{
    /// <summary>
    /// The identifier of the template to discard.
    /// </summary>
    [Description("The unique identifier of the template to discard.")]
    public required string Id { get; set; }
}

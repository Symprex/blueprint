using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Archives a template by its identifier. Demonstrates a key-only resource-event command
/// that produces an HTTP 201 Created with no body (identity-only event).
/// </summary>
[ApiOperations("Templates")]
[RootLink("/templates/{Id}")]
[HttpDelete]
[MustBeAuthenticated]
public partial class ArchiveTemplateCommand : ICommand<KeyOnlyResourceEventResult>
{
    /// <summary>
    /// The identifier of the template to archive (bound from the route).
    /// </summary>
    public required string Id { get; set; }
}

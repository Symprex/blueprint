using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Archives a template by its identifier. Demonstrates a void command (no response body)
/// that produces an HTTP 204 No Content response when executed successfully.
/// </summary>
[RootLink("/templates/{Id}")]
[HttpDelete]
[MustBeAuthenticated]
public partial class ArchiveTemplateCommand : ICommand
{
    /// <summary>
    /// The identifier of the template to archive (bound from the route).
    /// </summary>
    public required string Id { get; set; }
}

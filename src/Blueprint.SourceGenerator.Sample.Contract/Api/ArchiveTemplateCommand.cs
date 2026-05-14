using System.ComponentModel;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Archives a template by its identifier. Demonstrates a key-only resource-event command
/// that produces an HTTP 204 No Content with no body (identity-only event for a DELETE).
/// </summary>
/// <remarks>
/// Demonstrates <see cref="ResourceAttribute"/>-based authorization for a destructive command:
/// the user must satisfy the resource-scope <c>("template", "write")</c> check before the archive
/// operation can execute.
/// </remarks>
[ApiOperations("Templates")]
[Link(typeof(TemplateApiResource), "/templates/{Id}", Rel = "archive")]
[HttpDelete]
[Resource("template", "write")]
[Description("Archive (soft-delete) a template.")]
public partial class ArchiveTemplateCommand : ICommand<KeyOnlyResourceEventResult>
{
    /// <summary>
    /// The identifier of the template to archive (bound from the route).
    /// </summary>
    [Description("The unique identifier of the template to archive.")]
    public required string Id { get; set; }
}

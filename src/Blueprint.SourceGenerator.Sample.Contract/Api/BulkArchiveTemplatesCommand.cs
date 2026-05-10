using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Queues a bulk archive of templates by their IDs. Returns a bodyless <c>202 Accepted</c>
/// with no response payload, demonstrating the <see cref="StatusCodeResult.Accepted"/> pattern
/// for fire-and-forget async operations where the caller does not need a tracking resource.
/// <para>
/// This mirrors the legacy Blueprint pattern <c>ICommand&lt;StatusCodeResult.Accepted&gt;</c>
/// used extensively in Signature365 for background data-management operations.
/// </para>
/// </summary>
[ApiOperations("Templates")]
[RootLink("/templates/bulk-archive")]
[HttpPost]
[Resource("template", "write")]
[Description("Queue a bulk archive of templates by ID. Returns 202 with no body.")]
public partial class BulkArchiveTemplatesCommand : ICommand<StatusCodeResult.Accepted>
{
    /// <summary>
    /// The IDs of the templates to archive. At least one is required.
    /// </summary>
    [Required]
    [MinLength(1)]
    [MaxLength(100)]
    [Description("Template IDs to archive.")]
    public string[] Ids { get; set; } = [];

    /// <summary>
    /// An optional reason for the bulk archive, recorded in the audit trail.
    /// </summary>
    [MaxLength(500)]
    [Description("Optional reason for the bulk archive.")]
    public string? Reason { get; set; }
}

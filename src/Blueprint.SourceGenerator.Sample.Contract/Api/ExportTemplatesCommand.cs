using System.ComponentModel;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.Validation;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Queues a bulk export of all templates matching the optional filter. Returns a <c>202 Accepted</c>
/// response with an <see cref="ExportJobApiResource"/> tracking body, demonstrating the
/// <see cref="AcceptedResult{T}"/> pattern for long-running operations that complete asynchronously.
/// Also demonstrates conditional validation attributes:
/// <list type="bullet">
///   <item><see cref="RequiredIfAttribute"/> — <see cref="NotifyEmail"/> is required when <see cref="NotifyOnCompletion"/> is true</item>
///   <item><see cref="NotAllowedIfAttribute"/> — <see cref="NotifyEmail"/> cannot be set when <see cref="NotifyOnCompletion"/> is false</item>
///   <item><see cref="RequiredItemsIfAttribute"/> — <see cref="Formats"/> must be non-empty when <see cref="RestrictFormats"/> is true</item>
/// </list>
/// </summary>
[ApiOperations("Templates")]
[RootLink("/templates/export")]
[HttpPost]
[Resource("template", "read")]
[Description("Queue a bulk export of templates and receive a job tracking reference.")]
public partial class ExportTemplatesCommand : ICommand<AcceptedResult<ExportJobApiResource>>
{
    /// <summary>
    /// An optional name filter to restrict which templates are included in the export.
    /// When null or empty, all templates are exported.
    /// </summary>
    [Description("Optional name filter to restrict exported templates.")]
    public string? NameFilter { get; set; }

    /// <summary>
    /// Whether to send an email notification when the export job completes.
    /// When true, <see cref="NotifyEmail"/> becomes required.
    /// </summary>
    [Description("Send an email notification when the export completes.")]
    public bool NotifyOnCompletion { get; set; }

    /// <summary>
    /// The email address to notify. Required when <see cref="NotifyOnCompletion"/> is true (via
    /// <see cref="RequiredIfAttribute"/>), and not allowed when <see cref="NotifyOnCompletion"/>
    /// is false (via <see cref="NotAllowedIfAttribute"/>).
    /// </summary>
    [RequiredIf(nameof(NotifyOnCompletion), true)]
    [NotAllowedIf(nameof(NotifyOnCompletion), false)]
    [Blueprint.Validation.EmailAddress]
    [Description("Email address to notify on export completion. Required when NotifyOnCompletion is true.")]
    public string? NotifyEmail { get; set; }

    /// <summary>
    /// Whether to restrict the export to a specific set of formats. When true, <see cref="Formats"/> must
    /// contain at least one format.
    /// </summary>
    [Description("Restrict the export to a specific set of formats.")]
    public bool RestrictFormats { get; set; }

    /// <summary>
    /// The export formats to include. Required to contain at least one item when <see cref="RestrictFormats"/>
    /// is true (via <see cref="RequiredItemsIfAttribute"/>). Because the requirement is cross-property
    /// conditional, it is enforced at runtime and is intentionally absent from the OpenAPI schema.
    /// </summary>
    [RequiredItemsIf(nameof(RestrictFormats), true)]
    [Description("Export formats to include. Must contain at least one item when RestrictFormats is true.")]
    public List<string>? Formats { get; set; }
}

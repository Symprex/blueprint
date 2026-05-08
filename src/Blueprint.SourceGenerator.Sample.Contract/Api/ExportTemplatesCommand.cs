using System.ComponentModel;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Queues a bulk export of all templates matching the optional filter. Returns a <c>202 Accepted</c>
/// response with an <see cref="ExportJobApiResource"/> tracking body, demonstrating the
/// <see cref="AcceptedResult{T}"/> pattern for long-running operations that complete asynchronously.
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
}

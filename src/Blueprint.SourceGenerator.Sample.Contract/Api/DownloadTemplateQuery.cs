using System.ComponentModel;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Downloads a template as a binary file. Demonstrates <see cref="FileStreamResult"/> which
/// streams binary content with a configurable content type and optional <c>Content-Disposition</c>
/// attachment header for named downloads.
/// </summary>
[ApiOperations("Templates")]
[RootLink("/templates/{Id}/download")]
[HttpGet]
[Resource("template", "read")]
[Description("Download the template content as a file.")]
public partial class DownloadTemplateQuery : IQuery<FileStreamResult>
{
    /// <summary>
    /// The identifier of the template to download.
    /// </summary>
    [Description("The unique identifier of the template to download.")]
    public required string Id { get; set; }
}

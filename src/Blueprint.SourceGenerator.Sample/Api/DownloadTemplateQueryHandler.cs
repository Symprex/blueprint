using System.Text;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles the <see cref="DownloadTemplateQuery"/> by returning the template content as a binary
/// file download. Demonstrates how <see cref="FileStreamResult"/> streams content with a content
/// type and optional <c>Content-Disposition</c> attachment header.
/// </summary>
public class DownloadTemplateQueryHandler(TemplateStore store)
{
    /// <summary>
    /// Returns the template content as a downloadable file, or an empty placeholder when not found.
    /// </summary>
    /// <param name="query">The download query containing the template identifier.</param>
    /// <returns>A <see cref="FileStreamResult"/> wrapping the template content.</returns>
    public FileStreamResult Handle(DownloadTemplateQuery query)
    {
        var template = store.GetById(query.Id);
        var content = template?.HtmlSource ?? "Template not found.";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        return new FileStreamResult(stream, "application/octet-stream", $"template-{query.Id}.txt");
    }
}

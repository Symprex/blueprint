using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handler for <see cref="GetFolderContentsQuery"/>. Returns an <see cref="OkResult"/>
/// echoing the resolved folder path to prove catch-all route parameters work end-to-end.
/// </summary>
public class GetFolderContentsQueryHandler
{
    /// <summary>
    /// Returns the resolved folder path to prove catch-all routing works.
    /// </summary>
    /// <param name="operation">The query containing the path-like folder ID.</param>
    /// <returns>An OK result with the folder path.</returns>
    public OkResult Handle(GetFolderContentsQuery operation)
    {
        return new OkResult($"Contents of folder: {operation.FolderPath}");
    }
}

using System.ComponentModel;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Retrieves the contents of a folder identified by a path-like ID using ASP.NET Core catch-all
/// route parameters. The <c>{**FolderPath}</c> syntax allows values containing <c>/</c> to be
/// passed as a single route segment (e.g. <c>/api/folders/UK/Marketing/Archive</c>
/// resolves <c>FolderPath</c> to <c>"UK/Marketing/Archive"</c>).
/// </summary>
[ApiOperations("Folders")]
[RootLink("/folders/{**FolderPath}")]
[HttpGet]
[AllowAnonymous]
[Description("Get contents of a folder by its path.")]
public partial class GetFolderContentsQuery : IQuery<OkResult>
{
    /// <summary>
    /// The folder path, which may contain slashes (e.g. "UK/Marketing/Archive").
    /// Mapped from the catch-all route parameter <c>{**FolderPath}</c>.
    /// </summary>
    [Description("Path of the folder (may contain / separators).")]
    public string FolderPath { get; set; } = string.Empty;
}

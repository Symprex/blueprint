using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Tests.Integration.Upstream;

/// <summary>
/// Renames a note upstream. Its properties are documented only by their XML summaries, with no <c>[Description]</c>, as
/// Signature365's inner operations are (Symprex/blueprint#174).
/// </summary>
[RootLink("/notes/{NoteId}")]
[HttpPut]
public partial class RenameUpstreamNoteCommand : ICommand<UpstreamNoteApiResource>
{
    /// <summary>
    /// The note to rename, from the route.
    /// </summary>
    public string NoteId { get; set; } = string.Empty;

    /// <summary>
    /// The note's new title, over
    /// two lines, Tom &amp; Jerry.
    /// </summary>
    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// Finds notes upstream. Its property is documented only by its XML summary.
/// </summary>
[RootLink("/notes")]
[HttpGet]
public partial class FindUpstreamNotesQuery : IQuery<UpstreamNoteApiResource>
{
    /// <summary>
    /// Only notes whose title contains this text.
    /// </summary>
    public string? TitleContains { get; set; }
}

/// <summary>
/// A note upstream.
/// </summary>
public partial class UpstreamNoteApiResource : ApiResource
{
    /// <summary>
    /// The note's title.
    /// </summary>
    public string Title { get; set; } = string.Empty;
}

using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Tests.Integration.Upstream;

/// <summary>
/// Renames a note upstream. Its properties are documented only by their XML summaries, with no <c>[Description]</c>, as
/// Signature365's inner operations are (Symprex/blueprint#174).
/// </summary>
[RootLink("/notes/{NoteId}")]
[HttpPut]
public partial class RenameUpstreamNoteCommand : UpstreamNoteCommandBase, ICommand<UpstreamNoteApiResource>, IUpstreamPinnable
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

    /// <inheritdoc/>
    public override string? Colour { get; set; }

    /// <inheritdoc/>
    public bool Pinned { get; set; }

    /// <summary><inheritdoc cref="UpstreamNoteWording.Subtitle"/></summary>
    /// <remarks>The command's own remarks.</remarks>
    public string? Subtitle { get; set; }

    /// <summary><para><inheritdoc cref="UpstreamNoteWording.Subtitle"/></para></summary>
    public string? Caption { get; set; }

#pragma warning disable SA1648 // Inheriting what is not there is the point: the summary beside it is still used.
    /// <summary>
    /// The note's mood, with remarks it cannot inherit.
    /// </summary>
    /// <remarks><inheritdoc/></remarks>
    public string? Mood { get; set; }

    /// <summary>
    /// The note's shade, beside documentation it cannot inherit.
    /// </summary>
    /// <inheritdoc/>
    public string? Shade { get; set; }
#pragma warning restore SA1648
}

/// <summary>
/// Wording another member's documentation refers to.
/// </summary>
public static class UpstreamNoteWording
{
    /// <summary>
    /// The subtitle shown under the title.
    /// </summary>
    /// <remarks>Remarks a summary that refers here does not take.</remarks>
    public static string Subtitle => string.Empty;
}

/// <summary>
/// What every note command has, documented here rather than on the command that overrides it.
/// </summary>
public abstract class UpstreamNoteCommandBase
{
    /// <summary>
    /// The note's colour, documented on the base.
    /// </summary>
    public abstract string? Colour { get; set; }
}

/// <summary>
/// A note that can be pinned, documented here rather than on the command that implements it.
/// </summary>
public interface IUpstreamPinnable
{
    /// <summary>
    /// Whether the note is pinned, documented on the interface.
    /// </summary>
    bool Pinned { get; set; }
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

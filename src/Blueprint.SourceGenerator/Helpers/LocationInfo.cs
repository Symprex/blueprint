using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Blueprint.SourceGenerator.Helpers;

/// <summary>
/// A lightweight version of a <see cref="Location"/> that only contains the information
/// that is required for the source generator to work, whilst maintaining cacheability of the pipeline.
/// </summary>
/// <param name="FilePath">The path to the file.</param>
/// <param name="TextSpan">The text span of the location.</param>
/// <param name="LinePositionSpan">The line position span of the location.</param>
[DebuggerDisplay("{FilePath}: {LinePositionSpan.Start.Line}:{LinePositionSpan.Start.Character}-{LinePositionSpan.End.Line}:{LinePositionSpan.End.Character}")]
public sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LinePositionSpan)
{
    /// <summary>
    /// Creates a new <see cref="LocationInfo"/> that represents a sub-range of the current <see cref="LocationInfo"/>.
    /// </summary>
    /// <param name="index">The index from the start of the current <see cref="LocationInfo"/>.</param>
    /// <param name="length">The length of the sub-range.</param>
    /// <returns>A new <see cref="LocationInfo"/> representing the sub-range.</returns>
    public LocationInfo WithSubRange(int index, int length)
    {
        return this with
        {
            TextSpan = new TextSpan(this.TextSpan.Start + index, length),

            LinePositionSpan = new LinePositionSpan(
                new LinePosition(this.LinePositionSpan.Start.Line, this.LinePositionSpan.Start.Character + index),
                new LinePosition(this.LinePositionSpan.End.Line, this.LinePositionSpan.Start.Character + index + length)),
        };
    }

    /// <summary>
    /// Converts this <see cref="LocationInfo"/> to a <see cref="Location"/>.
    /// </summary>
    /// <returns>A <see cref="Location"/> representing this <see cref="LocationInfo"/>.</returns>
    public Location ToLocation()
    {
        return Location.Create(this.FilePath, this.TextSpan, this.LinePositionSpan);
    }

    /// <summary>
    /// Creates a <see cref="LocationInfo"/> from the given <see cref="SyntaxNode"/>.
    /// </summary>
    /// <param name="node">The node to create the <see cref="LocationInfo"/> from.</param>
    /// <returns>A <see cref="LocationInfo"/> representing the given <see cref="SyntaxNode"/>.</returns>
    public static LocationInfo? CreateFrom(SyntaxNode node)
    {
        return CreateFrom(node.GetLocation());
    }

    /// <summary>
    /// Creates a <see cref="LocationInfo"/> from the given <see cref="Location"/>.
    /// </summary>
    /// <param name="location">The location to create the <see cref="LocationInfo"/> from.</param>
    /// <returns>A <see cref="LocationInfo"/> representing the given <see cref="Location"/>.</returns>
    public static LocationInfo CreateFrom(Location location)
    {
        // We cannot create a "proper" LocationInfo if this comes from a metadata location as much of it is missing
        if (location.IsInMetadata)
        {
            return new LocationInfo(location.MetadataModule?.Name ?? string.Empty, location.SourceSpan, location.GetLineSpan().Span);
        }

        if (location.SourceTree is null)
        {
            throw new InvalidOperationException("Cannot create LocationInfo from a location with no SourceTree");
        }

        return new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }
}

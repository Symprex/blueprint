namespace Blueprint.Model;

/// <summary>
/// A link from one resource to another, that indicates what operations may be available
/// for a given <see cref="ILinkableResource" /> being returned from the API.
/// </summary>
public record Link
{
    /// <summary>
    /// The URL of this link.
    /// </summary>
    public required string Href { get; init; }

    /// <summary>
    /// The type of resource this link will return.
    /// </summary>
    /// <remarks>
    /// Written as <c>null</c> when a link has none, as legacy Blueprint wrote it: its only ignore was Newtonsoft's, and its
    /// HTTP output was System.Text.Json with no <c>DefaultIgnoreCondition</c> (Signature365 <c>8b40ecc961</c>,
    /// <c>Blueprint/Link.cs:19-23</c>, <c>Blueprint.Http/BlueprintJsonOptions.cs:22-45</c>).
    /// </remarks>
    public string? Type { get; set; }
}

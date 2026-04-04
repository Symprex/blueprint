using System.Text.Json.Serialization;

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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; set; }
}

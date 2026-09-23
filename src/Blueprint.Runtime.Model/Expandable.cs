using JetBrains.Annotations;

namespace Blueprint.Model;

/// <summary>
/// A container for a lazily-expanded resource reference. Initially holds only the
/// resource <see cref="Id"/>; when a client requests expansion via the <c>?expand=PropertyName</c>
/// query parameter (or when the property is marked <c>[AlwaysExpand]</c>), a registered
/// resource expander populates <see cref="Value"/>.
/// </summary>
/// <typeparam name="T">The type of the resource that can be expanded.</typeparam>
[PublicAPI]
public class Expandable<T>
    where T : ApiResource
{
    /// <summary>
    /// Initialises a new instance of the <see cref="Expandable{T}"/> class with the given resource identifier.
    /// </summary>
    /// <param name="id">The identifier of the resource to expand.</param>
    public Expandable(string id)
    {
        this.Id = id;
    }

    /// <summary>
    /// Gets the identifier of the expandable resource.
    /// </summary>
    public string Id { get; init; }

    /// <summary>
    /// Gets or sets the expanded resource value. This is <see langword="null"/> until a
    /// registered resource expander populates it.
    /// </summary>
    public T? Value { get; set; }

    /// <summary>
    /// Gets or sets the reason expansion was attempted but failed, for example <c>authentication_error</c> when the
    /// upstream system an expander calls rejects its credentials. <see cref="Value"/> is <see langword="null"/>
    /// whenever this is set, and clients read it to tell "could not be expanded" apart from "not expanded".
    /// </summary>
    /// <remarks>
    /// Serialised as <c>expandError</c>, and written as <see langword="null"/> when unset rather than omitted, which is
    /// the shape Signature365's legacy <c>Expandable&lt;T&gt;</c> had on the wire and in its OpenAPI document.
    /// </remarks>
    public string? ExpandError { get; set; }
}

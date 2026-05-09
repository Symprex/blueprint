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
}

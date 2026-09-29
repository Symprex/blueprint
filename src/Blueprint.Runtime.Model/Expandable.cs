using JetBrains.Annotations;

namespace Blueprint.Model;

/// <summary>
/// Represents an expandable resource property.
/// </summary>
/// <remarks>
/// A container for a lazily-expanded resource reference. Initially holds only the
/// resource <see cref="Id"/>; when a client requests expansion via the <c>?expand=PropertyName</c>
/// query parameter (or when the property is marked <c>[AlwaysExpand]</c>), a registered
/// resource expander populates <see cref="Value"/>.
/// The summaries of this type and its properties are legacy Blueprint's, which the OpenAPI document describes them with.
/// </remarks>
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
    /// The ID of this expandable value.
    /// </summary>
    public string Id { get; init; }

    /// <summary>
    /// The value itself, which will be <c>null</c> if the value has not been expanded.
    /// </summary>
    /// <remarks>
    /// Set by a registered resource expander; it stays <see langword="null"/> until then.
    /// </remarks>
    public T? Value { get; set; }

    /// <summary>
    /// When set, indicates that expansion was attempted but failed for the given reason.
    /// <see cref="Value"/> will be <c>null</c>.
    /// </summary>
    /// <remarks>
    /// The reason is for example <c>authentication_error</c> when the upstream system an expander calls rejects its
    /// credentials; clients read it to tell "could not be expanded" apart from "not expanded".
    /// Serialised as <c>expandError</c>, and written as <see langword="null"/> when unset rather than omitted, which is
    /// the shape Signature365's legacy <c>Expandable&lt;T&gt;</c> had on the wire and in its OpenAPI document.
    /// </remarks>
    public string? ExpandError { get; set; }
}

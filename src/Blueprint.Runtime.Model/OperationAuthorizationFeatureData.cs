using System.Collections.Immutable;

namespace Blueprint.Model;

/// <summary>
/// Describes the authorization scope requirements for an operation, determined at compile time
/// from <c>[Resource("noun", "verb")]</c> attributes. Multiple scope pairs on a single operation
/// use any-of semantics: the caller must satisfy at least one declared pair.
/// </summary>
/// <remarks>
/// <para>
/// This is everything the attributes declare, so an app that needs it, for example to map roles to operations or list an
/// operation's permissions, reads it here rather than reflecting over the attributes itself.
/// </para>
/// <para>
/// For a <c>[FromResource]</c> operation, <see cref="ScopePairs"/> are the source operation's direct <c>[Resource]</c>
/// pairs, one level and not transitive, each carrying <see cref="InheritedTemplateOverride"/> when one was given and the
/// source's own template otherwise.
/// </para>
/// </remarks>
/// <param name="ScopePairs">
/// The set of noun+verb scope pairs declared on the operation. Multiple entries
/// indicate any-of semantics: satisfying any one pair grants access.
/// </param>
public sealed record OperationAuthorizationFeatureData(ImmutableArray<ScopePair> ScopePairs)
{
    /// <summary>
    /// Gets the operation whose <c>[Resource]</c> pairs this one inherits through <c>[FromResource]</c>, or
    /// <see langword="null"/> when its pairs are declared directly.
    /// </summary>
    public Type? InheritedFrom { get; init; }

    /// <summary>
    /// Gets the resource key template <c>[FromResource]</c> gave to override the source operation's templates, or
    /// <see langword="null"/> when it gave none or the pairs are declared directly.
    /// </summary>
    public string? InheritedTemplateOverride { get; init; }
}

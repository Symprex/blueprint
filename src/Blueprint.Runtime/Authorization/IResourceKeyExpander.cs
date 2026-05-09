using JetBrains.Annotations;

namespace Blueprint.Authorization;

/// <summary>
/// Expands a resource key to include its full hierarchical path. For example,
/// given a resource key "User/1", an expander might return "Account/6/Team/5/User/1"
/// to support hierarchical permission inheritance.
/// </summary>
/// <remarks>
/// <para>
/// Resource key expanders allow permissions granted at a higher level in a hierarchy
/// to automatically cover child resources. For example, a permission granted on "Account/6"
/// would satisfy a demand for "Account/6/Team/5/User/1" because the granted claim's value
/// is a prefix of the demanded claim's value.
/// </para>
/// <para>
/// Implementations are resolved from DI by <see cref="ClaimInspector"/>.
/// </para>
/// </remarks>
[PublicAPI]
public interface IResourceKeyExpander
{
    /// <summary>
    /// Expands the given resource key to its full hierarchical path.
    /// </summary>
    /// <param name="resourceKey">The resource key to expand.</param>
    /// <returns>
    /// The expanded key (e.g. "Account/6/Team/5/User/1") or <see langword="null"/>
    /// if this expander cannot expand the given key.
    /// </returns>
    string? Expand(string resourceKey);
}

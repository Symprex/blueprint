namespace Blueprint.Model;

/// <summary>
/// Resolves an operation's scope pairs against a resource a link to the operation would be written on, so the resource
/// authorization providers can decide whether the link is shown. Generated for every operation that declares scope pairs
/// and links from at least one resource type.
/// </summary>
/// <remarks>
/// <para>
/// The generator emits one resolver per resource type the operation links from (<c>[Link]</c> and <c>[SelfLink]</c>),
/// most-derived type first, with property access in place of reflection. A <c>{A:B}</c> token uses <c>A</c> when that
/// resource type has a public property <c>A</c>, and <c>B</c> only when it has not: whether the property exists decides,
/// not whether its value is <see langword="null"/>. A null value is written as an empty segment.
/// </para>
/// <para>
/// It lives with the descriptor, in this assembly, because descriptors are generated wherever an operation is declared,
/// including contract assemblies that reference nothing else of Blueprint. So the caller's values for <c>{Auth.X}</c>
/// tokens are looked up through a function the runtime supplies, not read from its types.
/// </para>
/// </remarks>
/// <param name="resolve">
/// Given the resource and a lookup of the caller's values by <c>{Auth.X}</c> name, the operation's scope pairs with their
/// keys resolved against the resource, or <see langword="null"/> when the resource is of no type the operation links from.
/// </param>
public sealed class OperationLinkAuthorizationFeatureData(
    Func<object, Func<string, string?>, IReadOnlyList<ResolvedScopePair>?> resolve)
{
    /// <summary>
    /// Resolves the operation's scope pairs against <paramref name="resource"/>.
    /// </summary>
    /// <param name="resource">The resource the link would be written on.</param>
    /// <param name="callerValue">Gives the caller's value for an <c>{Auth.X}</c> token's name, or <see langword="null"/>.</param>
    /// <returns>The resolved scope pairs, or <see langword="null"/> when the operation does not link from the resource's type.</returns>
    public IReadOnlyList<ResolvedScopePair>? Resolve(object resource, Func<string, string?> callerValue)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(callerValue);

        return resolve(resource, callerValue);
    }
}

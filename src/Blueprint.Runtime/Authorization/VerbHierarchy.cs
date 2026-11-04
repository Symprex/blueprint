using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Blueprint.Authorization;

/// <summary>
/// An app's verb hierarchy: which verbs a broader verb includes, so that a scope pair carrying it also satisfies a
/// requirement for any of them (for example <c>Manage</c> including <c>Create</c>, <c>Read</c>, <c>Update</c> and
/// <c>Delete</c>). Inclusion is transitive, and computed once when the hierarchy is built.
/// </summary>
/// <remarks>
/// <para>
/// One instance is meant to be shared by everything that applies the app's policy to scope pairs:
/// <see cref="ScopePairClaimAuthorizationProvider"/>, which decides a token's scopes, and
/// <see cref="ScopePairOperationIndex"/>, which maps a pair to the operations it satisfies, for example to map roles.
/// </para>
/// <para>
/// A cycle (<c>A → B → A</c>) is tolerated: the walk stops where it meets a verb already reached, and no verb is listed as
/// including, or included in, itself.
/// </para>
/// </remarks>
public sealed class VerbHierarchy
{
    private readonly FrozenDictionary<string, ImmutableArray<string>> _includes;
    private readonly FrozenDictionary<string, ImmutableArray<string>> _includedIn;
    private readonly FrozenSet<string> _verbs;

    /// <summary>
    /// Initialises a new instance of the <see cref="VerbHierarchy"/> class.
    /// </summary>
    /// <param name="verbInclusions">
    /// Maps each parent verb to the child verbs it includes, for example <c>{ "Manage": ["Create", "Read", "Update",
    /// "Delete"] }</c>. Inclusion is transitive: with <c>Admin → [Manage]</c> and <c>Manage → [Read]</c>, <c>Admin</c>
    /// includes <c>Read</c> too. A parent with no children names a verb the hierarchy knows that includes nothing. Parents
    /// that are equal under <paramref name="comparison"/> are merged.
    /// </param>
    /// <param name="comparison">How verbs are compared.</param>
    /// <exception cref="ArgumentNullException"><paramref name="verbInclusions"/> is <see langword="null"/>.</exception>
    public VerbHierarchy(IDictionary<string, string[]> verbInclusions, StringComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(verbInclusions);

        var comparer = StringComparer.FromComparison(comparison);

        this.Comparison = comparison;

        var children = new Dictionary<string, string[]>(comparer);

        foreach (var (parentVerb, childVerbs) in verbInclusions)
        {
            // Keys differing only in case were accepted before inclusion became transitive, so merge them, not throw.
            children[parentVerb] = children.TryGetValue(parentVerb, out var existing) ? [.. existing, .. childVerbs] : childVerbs;
        }

        var includes = new Dictionary<string, HashSet<string>>(comparer);
        var includedIn = new Dictionary<string, HashSet<string>>(comparer);
        var verbs = new HashSet<string>(children.Keys, comparer);

        foreach (var parentVerb in children.Keys)
        {
            // The visited set ends a walk that meets a cycle, and holds the parent so it never includes itself.
            var visited = new HashSet<string>(comparer) { parentVerb };
            var pending = new Stack<string>(children[parentVerb]);

            while (pending.Count > 0)
            {
                var includedVerb = pending.Pop();

                verbs.Add(includedVerb);

                if (!visited.Add(includedVerb))
                {
                    continue;
                }

                Add(includes, parentVerb, includedVerb, comparer);
                Add(includedIn, includedVerb, parentVerb, comparer);

                if (children.TryGetValue(includedVerb, out var grandchildren))
                {
                    foreach (var grandchild in grandchildren)
                    {
                        pending.Push(grandchild);
                    }
                }
            }
        }

        this._includes = Freeze(includes, comparer);
        this._includedIn = Freeze(includedIn, comparer);
        this._verbs = verbs.ToFrozenSet(comparer);
    }

    /// <summary>
    /// Gets how verbs are compared.
    /// </summary>
    public StringComparison Comparison { get; }

    /// <summary>
    /// Gets the verbs <paramref name="verb"/> includes, directly or transitively, not counting itself: the narrower verbs a
    /// scope pair carrying it also satisfies. Empty for a verb that includes nothing or that the hierarchy does not name.
    /// </summary>
    /// <param name="verb">The verb to look up.</param>
    /// <returns>The verbs it includes, in no particular order.</returns>
    public ImmutableArray<string> Includes(string verb)
    {
        return this._includes.TryGetValue(verb, out var included) ? included : [];
    }

    /// <summary>
    /// Gets the verbs that include <paramref name="verb"/>, directly or transitively, not counting itself: the broader
    /// verbs whose scope pair also satisfies a requirement for it. Empty for a verb nothing includes or that the hierarchy
    /// does not name.
    /// </summary>
    /// <param name="verb">The verb to look up.</param>
    /// <returns>The verbs that include it, in no particular order.</returns>
    public ImmutableArray<string> IncludedIn(string verb)
    {
        return this._includedIn.TryGetValue(verb, out var parents) ? parents : [];
    }

    /// <summary>
    /// Gets whether the hierarchy names <paramref name="verb"/>, as a parent (with or without children) or as a child.
    /// </summary>
    /// <param name="verb">The verb to look up.</param>
    /// <returns><see langword="true"/> when the hierarchy names it.</returns>
    public bool Contains(string verb)
    {
        return this._verbs.Contains(verb);
    }

    private static void Add(Dictionary<string, HashSet<string>> map, string key, string value, StringComparer comparer)
    {
        if (!map.TryGetValue(key, out var values))
        {
            values = new HashSet<string>(comparer);
            map[key] = values;
        }

        values.Add(value);
    }

    private static FrozenDictionary<string, ImmutableArray<string>> Freeze(Dictionary<string, HashSet<string>> map, StringComparer comparer)
    {
        return map.ToFrozenDictionary(
            static kvp => kvp.Key,
            static kvp => ImmutableCollectionsMarshal.AsImmutableArray(kvp.Value.ToArray()),
            comparer);
    }
}

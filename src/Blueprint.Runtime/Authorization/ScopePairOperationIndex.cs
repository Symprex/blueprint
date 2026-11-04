using System.Collections.Frozen;
using System.Collections.Immutable;
using Blueprint.Model;

namespace Blueprint.Authorization;

/// <summary>
/// Maps scope pairs to the operations they satisfy, and operations to the scope pairs that satisfy them, under an app's
/// <see cref="VerbHierarchy"/>: what an app needs to turn a role's pairs into the operations it may run. Built once from
/// the operations' <see cref="OperationAuthorizationFeatureData"/>, and read-only after.
/// </summary>
/// <remarks>
/// <para>
/// A pair <c>noun + verb</c> satisfies an operation when the operation declares a pair with the same noun and either the
/// same verb or one the hierarchy says the held verb includes, which is the match
/// <see cref="ScopePairClaimAuthorizationProvider"/> makes given the same hierarchy. Nouns and verbs are compared as
/// <see cref="VerbHierarchy.Comparison"/>. Resource key templates play no part: a pair is matched on its noun and verb only.
/// </para>
/// <para>
/// An operation that declares no scope pair is in no pair's operations. The generated feature data stays what the
/// operation declares; this index is where the app's policy, its hierarchy, is applied to it.
/// </para>
/// </remarks>
public sealed class ScopePairOperationIndex
{
    private readonly FrozenDictionary<(string Noun, string Verb), ImmutableArray<ApiOperationDescriptor>> _operationsByPair;
    private readonly FrozenDictionary<ApiOperationDescriptor, ImmutableArray<ScopePair>> _pairsByOperation;
    private readonly VerbHierarchy _verbHierarchy;
    private readonly PairComparer _pairComparer;

    /// <summary>
    /// Initialises a new instance of the <see cref="ScopePairOperationIndex"/> class over every operation of
    /// <paramref name="app"/>.
    /// </summary>
    /// <param name="app">The app whose operations are indexed.</param>
    /// <param name="verbHierarchy">
    /// The app's verb hierarchy, the instance given to <see cref="ScopePairClaimAuthorizationProvider"/>.
    /// </param>
    /// <param name="requireKnownVerbs">
    /// When <see langword="true"/>, an operation declaring a verb <paramref name="verbHierarchy"/> does not name is refused
    /// here, at construction, rather than being an operation only a pair with exactly that verb satisfies. Defaults to
    /// <see langword="false"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> or <paramref name="verbHierarchy"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="requireKnownVerbs"/> is <see langword="true"/> and an operation declares a verb the hierarchy does not
    /// name. The message lists each such operation, noun and verb.
    /// </exception>
    public ScopePairOperationIndex(BlueprintApp app, VerbHierarchy verbHierarchy, bool requireKnownVerbs = false)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(verbHierarchy);

        this._verbHierarchy = verbHierarchy;
        this._pairComparer = new PairComparer(StringComparer.FromComparison(verbHierarchy.Comparison));

        var operationsByPair = new Dictionary<(string Noun, string Verb), List<ApiOperationDescriptor>>(this._pairComparer);
        var pairsByOperation = new Dictionary<ApiOperationDescriptor, ImmutableArray<ScopePair>>();
        var unknownVerbs = new List<string>();

        foreach (var operation in app.Operations)
        {
            if (!operation.TryGetFeatureData<OperationAuthorizationFeatureData>(out var authorization) || authorization.ScopePairs.IsDefaultOrEmpty)
            {
                continue;
            }

            if (requireKnownVerbs)
            {
                foreach (var pair in authorization.ScopePairs)
                {
                    if (!verbHierarchy.Contains(pair.Verb))
                    {
                        unknownVerbs.Add($"{operation.OperationType.FullName} ({pair.Noun}, {pair.Verb})");
                    }
                }
            }

            var satisfiedBy = this.Expand(authorization.ScopePairs);

            pairsByOperation[operation] = satisfiedBy;

            foreach (var pair in satisfiedBy)
            {
                if (!operationsByPair.TryGetValue((pair.Noun, pair.Verb), out var operations))
                {
                    operations = [];
                    operationsByPair[(pair.Noun, pair.Verb)] = operations;
                }

                operations.Add(operation);
            }
        }

        if (unknownVerbs.Count > 0)
        {
            throw new InvalidOperationException(
                $"These operations declare a verb the verb hierarchy does not name: {string.Join(", ", unknownVerbs)}");
        }

        this._operationsByPair = operationsByPair.ToFrozenDictionary(
            static kvp => kvp.Key,
            static kvp => kvp.Value.ToImmutableArray(),
            this._pairComparer);
        this._pairsByOperation = pairsByOperation.ToFrozenDictionary();
    }

    /// <summary>
    /// Gets the operations a caller holding <paramref name="pair"/> satisfies: those declaring its noun with its verb or a
    /// verb its verb includes. The pair's <see cref="ScopePair.ResourceKeyTemplate"/> is ignored.
    /// </summary>
    /// <param name="pair">The scope pair held, for example one of a role's pairs.</param>
    /// <returns>The operations it satisfies, each once, in no particular order; empty when it satisfies none.</returns>
    public ImmutableArray<ApiOperationDescriptor> OperationsFor(ScopePair pair)
    {
        return this._operationsByPair.TryGetValue((pair.Noun, pair.Verb), out var operations) ? operations : [];
    }

    /// <summary>
    /// Gets the scope pairs any one of which satisfies <paramref name="descriptor"/>: each pair it declares, and the same
    /// noun with each verb that includes the declared verb. The pairs carry no <see cref="ScopePair.ResourceKeyTemplate"/>.
    /// </summary>
    /// <param name="descriptor">The operation, normally one of the app's.</param>
    /// <returns>The satisfying pairs, each once, in no particular order; empty for an operation that declares none.</returns>
    public ImmutableArray<ScopePair> SatisfiedBy(ApiOperationDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (this._pairsByOperation.TryGetValue(descriptor, out var pairs))
        {
            return pairs;
        }

        return descriptor.TryGetFeatureData<OperationAuthorizationFeatureData>(out var authorization) && !authorization.ScopePairs.IsDefaultOrEmpty
            ? this.Expand(authorization.ScopePairs)
            : [];
    }

    private ImmutableArray<ScopePair> Expand(ImmutableArray<ScopePair> declared)
    {
        var seen = new HashSet<(string Noun, string Verb)>(this._pairComparer);
        var builder = ImmutableArray.CreateBuilder<ScopePair>();

        foreach (var pair in declared)
        {
            if (seen.Add((pair.Noun, pair.Verb)))
            {
                builder.Add(new ScopePair(pair.Noun, pair.Verb));
            }

            foreach (var parentVerb in this._verbHierarchy.IncludedIn(pair.Verb))
            {
                if (seen.Add((pair.Noun, parentVerb)))
                {
                    builder.Add(new ScopePair(pair.Noun, parentVerb));
                }
            }
        }

        return builder.ToImmutable();
    }

    private sealed class PairComparer(StringComparer comparer) : IEqualityComparer<(string Noun, string Verb)>
    {
        public bool Equals((string Noun, string Verb) x, (string Noun, string Verb) y)
        {
            return comparer.Equals(x.Noun, y.Noun) && comparer.Equals(x.Verb, y.Verb);
        }

        public int GetHashCode((string Noun, string Verb) obj)
        {
            return HashCode.Combine(comparer.GetHashCode(obj.Noun), comparer.GetHashCode(obj.Verb));
        }
    }
}

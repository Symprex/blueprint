using System.Collections;

namespace Blueprint.Authorization;

/// <summary>
/// The grants a principal holds for one operation, as an immutable set: it covers a key when any grant in it does.
/// </summary>
public sealed class ResourceGrants : IReadOnlyCollection<ResourceGrant>
{
    private readonly ResourceGrant[] _grants;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceGrants"/> class.
    /// </summary>
    /// <param name="grants">The grants. A grant given more than once is held once, and order is kept.</param>
    public ResourceGrants(IEnumerable<ResourceGrant> grants)
    {
        ArgumentNullException.ThrowIfNull(grants);

        this._grants = grants.Distinct().ToArray();
    }

    /// <summary>
    /// Gets the set that holds no grants and so covers nothing.
    /// </summary>
    public static ResourceGrants Empty { get; } = new([]);

    /// <inheritdoc/>
    public int Count => this._grants.Length;

    /// <summary>
    /// Determines whether any grant covers <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The key being asked about.</param>
    /// <returns><see langword="true"/> if a grant covers the key.</returns>
    /// <seealso cref="ResourceGrant.Covers"/>
    public bool Covers(ResourceKey key)
    {
        foreach (var grant in this._grants)
        {
            if (grant.Covers(key))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether any grant is not ruled out by the literal prefix of a demanded key. For listing an operation
    /// before any resource exists only: never use it for an execution decision.
    /// </summary>
    /// <param name="resourceKeyPrefix">The literal prefix of the demanded key.</param>
    /// <returns><see langword="true"/> if the prefix does not rule out every grant.</returns>
    /// <seealso cref="ResourceGrant.IsPrefixCompatibleWith"/>
    public bool IsPrefixCompatibleWith(string resourceKeyPrefix)
    {
        foreach (var grant in this._grants)
        {
            if (grant.IsPrefixCompatibleWith(resourceKeyPrefix))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public IEnumerator<ResourceGrant> GetEnumerator()
    {
        return ((IEnumerable<ResourceGrant>)this._grants).GetEnumerator();
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return this.GetEnumerator();
    }
}

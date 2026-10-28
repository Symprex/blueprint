using System.Diagnostics.CodeAnalysis;

namespace Blueprint.Authorization;

/// <summary>
/// A resource key a principal holds. It is always absolute (starting with <c>/</c>). A grant ending in <c>/*</c> covers
/// its base key and every key beneath it; any other grant covers only its own key.
/// </summary>
/// <remarks>
/// <para>
/// The wildcard only ever stops at a segment boundary: <c>/Tenants/ten_1/*</c> covers <c>/Tenants/ten_1</c> and
/// <c>/Tenants/ten_1/Users/usr_1</c>, but never <c>/Tenants/ten_12</c>. <c>/*</c> alone covers every absolute key, so it
/// is the root grant; there is no separate global key. A <c>*</c> anywhere else is literal. Comparison is ordinal.
/// </para>
/// <para>
/// Use <see cref="TryParse"/> for grants built from stored data, so that one malformed key is skipped instead of failing
/// every request made by its holder.
/// </para>
/// </remarks>
public readonly struct ResourceGrant : IEquatable<ResourceGrant>
{
    private const string DescendantsSuffix = "/*";

    private readonly string? _value;

    private ResourceGrant(string value)
    {
        this._value = value;
    }

    /// <summary>
    /// Gets the grant as given, including any trailing <c>/*</c>.
    /// </summary>
    public string Value => this._value ?? string.Empty;

    /// <summary>
    /// Gets a value indicating whether the grant ends in <c>/*</c>, and so covers every key beneath its base as well as
    /// the base itself.
    /// </summary>
    public bool IncludesDescendants => this.Value.EndsWith(DescendantsSuffix, StringComparison.Ordinal);

    // The key the grant is anchored at: the value without a trailing /*, which is empty for the root grant /*. A span over
    // the value, so matching allocates nothing.
    private ReadOnlySpan<char> Base => this.IncludesDescendants
        ? this.Value.AsSpan(0, this.Value.Length - DescendantsSuffix.Length)
        : this.Value.AsSpan();

    /// <summary>
    /// Compares two grants ordinally.
    /// </summary>
    /// <param name="left">The first grant.</param>
    /// <param name="right">The second grant.</param>
    /// <returns><see langword="true"/> if the grants are equal.</returns>
    public static bool operator ==(ResourceGrant left, ResourceGrant right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Compares two grants ordinally.
    /// </summary>
    /// <param name="left">The first grant.</param>
    /// <param name="right">The second grant.</param>
    /// <returns><see langword="true"/> if the grants differ.</returns>
    public static bool operator !=(ResourceGrant left, ResourceGrant right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// Parses a grant.
    /// </summary>
    /// <param name="value">The grant, which must start with <c>/</c>.</param>
    /// <returns>The grant.</returns>
    /// <exception cref="FormatException"><paramref name="value"/> is not absolute.</exception>
    public static ResourceGrant Parse(string value)
    {
        if (TryParse(value, out var grant))
        {
            return grant;
        }

        throw new FormatException($"Resource grant '{value}' is not absolute. A grant must start with '/'; use '/*' to grant every resource.");
    }

    /// <summary>
    /// Parses a grant without throwing.
    /// </summary>
    /// <param name="value">The grant.</param>
    /// <param name="grant">The parsed grant, or <see langword="default"/> when <paramref name="value"/> is not absolute.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is absolute.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, out ResourceGrant grant)
    {
        if (value is ['/', ..])
        {
            grant = new ResourceGrant(value);

            return true;
        }

        grant = default;

        return false;
    }

    /// <summary>
    /// Determines whether this grant covers <paramref name="key"/>: an exact grant covers only its own key, and a grant
    /// ending in <c>/*</c> covers its base and every key beneath it at a segment boundary. A key that is not absolute is
    /// never covered.
    /// </summary>
    /// <param name="key">The key being asked about.</param>
    /// <returns><see langword="true"/> if the grant covers the key.</returns>
    public bool Covers(ResourceKey key)
    {
        if (!key.IsAbsolute)
        {
            return false;
        }

        if (!this.IncludesDescendants)
        {
            return string.Equals(this.Value, key.Value, StringComparison.Ordinal);
        }

        var demanded = key.Value.AsSpan();
        var grantBase = this.Base;

        return demanded.StartsWith(grantBase, StringComparison.Ordinal)
               && (demanded.Length == grantBase.Length || demanded[grantBase.Length] == '/');
    }

    /// <summary>
    /// Determines whether this grant is not ruled out by the known literal prefix of a demanded key, that is a resource key
    /// template cut at its first unresolved placeholder. For listing an operation before any resource exists only: never
    /// use it for an execution decision.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Given the prefix <c>/Tenants/ten_1/Users/</c>, the grant's base may be that resource itself
    /// (<c>/Tenants/ten_1/Users</c>), an ancestor of it (<c>/Tenants/ten_1</c>, or empty for <c>/*</c>), or lie beneath it
    /// (<c>/Tenants/ten_1/Users/usr_1</c>). Every comparison stops at a segment boundary, so <c>/Tenants/ten_1/*</c> never
    /// matches the prefix <c>/Tenants/ten_12/</c>. Comparison is ordinal.
    /// </para>
    /// <para>
    /// It never rejects a grant that execution could honour, but it accepts two shapes that execution never honours, so an
    /// operation can be listed and still refused: an exact grant that is an ancestor of the prefix (<c>/Partners/p</c> for
    /// <c>/Partners/p/Users/</c>), and a grant beneath the prefix that diverges from the rest of the template
    /// (<c>/Tenants/ten_1/Groups/*</c> for <c>/Tenants/{TenantId}/Users</c>).
    /// </para>
    /// </remarks>
    /// <param name="resourceKeyPrefix">The literal prefix of the demanded key, such as <c>/Tenants/ten_1/Users/</c>.</param>
    /// <returns><see langword="true"/> if the prefix does not rule the grant out.</returns>
    public bool IsPrefixCompatibleWith(string resourceKeyPrefix)
    {
        ArgumentNullException.ThrowIfNull(resourceKeyPrefix);

        if (this._value == null)
        {
            return false;
        }

        var grantBase = this.Base;
        var prefix = resourceKeyPrefix.AsSpan();
        var prefixEndsWithSlash = prefix is [.., '/'];
        var trimmedPrefix = prefixEndsWithSlash ? prefix[..^1] : prefix;

        // The middle case is "the prefix starts with the base followed by /", compared without building that string
        return grantBase.Equals(trimmedPrefix, StringComparison.Ordinal)
               || (prefix.Length > grantBase.Length && prefix.StartsWith(grantBase, StringComparison.Ordinal) && prefix[grantBase.Length] == '/')
               || (prefixEndsWithSlash && grantBase.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <inheritdoc/>
    public bool Equals(ResourceGrant other)
    {
        return string.Equals(this.Value, other.Value, StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is ResourceGrant other && this.Equals(other);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(this.Value);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return this.Value;
    }
}

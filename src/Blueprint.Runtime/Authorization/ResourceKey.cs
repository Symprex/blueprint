using System.Diagnostics.CodeAnalysis;

namespace Blueprint.Authorization;

/// <summary>
/// A resource key being asked about: the key an operation demands access to, such as
/// <c>/Tenants/ten_1/Users/usr_1</c>, or a partial key such as <c>Users/usr_1</c> that an expander must make absolute
/// before it can be matched.
/// </summary>
/// <remarks>
/// <para>
/// A key is held verbatim. A <c>*</c> in it has no meaning, because a demanded key can carry property values the caller
/// controls; only a <see cref="ResourceGrant"/> gives <c>/*</c> a meaning. Equality is ordinal.
/// </para>
/// <para>
/// Not to be confused with <see cref="ResourceKey{T}"/>, which parses a single <c>[Type]/[Id]</c> pair into a typed id.
/// </para>
/// <para>
/// <c>default(ResourceKey)</c> is the empty key: it is not absolute, so no grant covers it.
/// </para>
/// </remarks>
public readonly struct ResourceKey : IEquatable<ResourceKey>
{
    private readonly string? _value;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceKey"/> struct.
    /// </summary>
    /// <param name="value">The key, absolute (starting with <c>/</c>) or partial.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> is <see langword="null"/> or empty.</exception>
    public ResourceKey(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        this._value = value;
    }

    /// <summary>
    /// Gets the key as given.
    /// </summary>
    public string Value => this._value ?? string.Empty;

    /// <summary>
    /// Gets a value indicating whether the key is absolute, that is whether it starts with <c>/</c>. Only an absolute
    /// key can be covered by a <see cref="ResourceGrant"/>.
    /// </summary>
    public bool IsAbsolute => this._value is ['/', ..];

    /// <summary>
    /// Determines whether this key is <paramref name="ancestor"/> itself or a key beneath it, such as
    /// <c>/Tenants/ten_1/Users/usr_1</c> within <c>/Tenants/ten_1</c>. Use it to hold a key an app was given, such as a
    /// stored role's, to the part of the hierarchy it belongs in; <see cref="ResourceGrant.IsWithin"/> does the same for a
    /// grant.
    /// </summary>
    /// <remarks>
    /// The comparison stops at a segment boundary, so <c>/Tenants/ten_12</c> is not within <c>/Tenants/ten_1</c>. It is
    /// ordinal, as grant matching is, so a key that differs only in case is not within. Both keys must be absolute: a
    /// partial key has no place in the hierarchy until it is expanded. A <c>*</c> is literal, as everywhere in a key.
    /// </remarks>
    /// <param name="ancestor">The key this one must be within, such as <c>/Tenants/ten_1</c>.</param>
    /// <returns><see langword="true"/> if this key is <paramref name="ancestor"/> or beneath it.</returns>
    public bool IsWithin(ResourceKey ancestor)
    {
        return this.IsAbsolute && ancestor.IsAbsolute && IsAtOrBeneath(this.Value.AsSpan(), ancestor.Value.AsSpan());
    }

    /// <summary>
    /// Gets the key's top-level segment as a prefix, such as <c>/Tenants/</c> for <c>/Tenants/{Id}/Users</c>: the part of
    /// the hierarchy a key, or a template held as one, belongs to, which an app may restrict a kind of role to.
    /// </summary>
    /// <remarks>
    /// <c>/</c> gives <c>/</c>. A <c>*</c> is literal, so <c>/*</c> gives <c>/*/</c>; the root grant is a
    /// <see cref="ResourceGrant"/>, not a key.
    /// </remarks>
    /// <param name="prefix">The prefix, starting and ending with <c>/</c>, or <see langword="null"/> for a partial key.</param>
    /// <returns><see langword="true"/> if the key is absolute, and so has a top-level segment.</returns>
    public bool TryGetTopLevelPrefix([NotNullWhen(true)] out string? prefix)
    {
        if (!this.IsAbsolute)
        {
            prefix = null;

            return false;
        }

        // Exactly one leading '/': an empty first segment, as in //Tenants, is a segment of its own, not part of the slash
        var segments = this.Value.AsSpan(1);
        var end = segments.IndexOf('/');

        prefix = segments.IsEmpty ? "/" : string.Concat("/", end < 0 ? segments : segments[..end], "/");

        return true;
    }

    /// <summary>
    /// Whether <paramref name="key"/> is <paramref name="ancestor"/> or beneath it at a segment boundary, compared
    /// ordinally. An empty ancestor is never matched, as no absolute key equals it.
    /// </summary>
    /// <param name="key">The key, or a grant's base.</param>
    /// <param name="ancestor">The key it must be within.</param>
    /// <returns><see langword="true"/> if <paramref name="key"/> is <paramref name="ancestor"/> or beneath it.</returns>
    internal static bool IsAtOrBeneath(ReadOnlySpan<char> key, ReadOnlySpan<char> ancestor)
    {
        return !ancestor.IsEmpty
               && key.StartsWith(ancestor, StringComparison.Ordinal)
               && (key.Length == ancestor.Length || key[ancestor.Length] == '/');
    }

    /// <summary>
    /// Compares two keys ordinally.
    /// </summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    /// <returns><see langword="true"/> if the keys are equal.</returns>
    public static bool operator ==(ResourceKey left, ResourceKey right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Compares two keys ordinally.
    /// </summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    /// <returns><see langword="true"/> if the keys differ.</returns>
    public static bool operator !=(ResourceKey left, ResourceKey right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc/>
    public bool Equals(ResourceKey other)
    {
        return string.Equals(this.Value, other.Value, StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is ResourceKey other && this.Equals(other);
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

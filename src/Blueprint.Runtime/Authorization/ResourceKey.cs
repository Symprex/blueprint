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

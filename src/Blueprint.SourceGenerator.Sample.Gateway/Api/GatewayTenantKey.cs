using System.Diagnostics.CodeAnalysis;

namespace Blueprint.SourceGenerator.Sample.Gateway.Api;

/// <summary>
/// The gateway's own tenant identifier: <c>tk_</c> followed by the tenant, as Signature365's prefixed <c>ApiId</c>. It does
/// not convert to the inner service's <c>string</c> tenant, so <see cref="AddGatewayKeyedTenantNoteCommand"/> converts it
/// in its map hook.
/// </summary>
/// <param name="Value">The tenant, without the prefix.</param>
public readonly record struct GatewayTenantKey(string Value) : IParsable<GatewayTenantKey>
{
    private const string Prefix = "tk_";

    /// <inheritdoc />
    public static GatewayTenantKey Parse(string s, IFormatProvider? provider)
    {
        return TryParse(s, provider, out var key) ? key : throw new FormatException($"'{s}' is not a tenant key.");
    }

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out GatewayTenantKey result)
    {
        if (s is not null && s.StartsWith(Prefix, StringComparison.Ordinal) && s.Length > Prefix.Length)
        {
            result = new GatewayTenantKey(s[Prefix.Length..]);
            return true;
        }

        result = default;
        return false;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Prefix + this.Value;
    }
}

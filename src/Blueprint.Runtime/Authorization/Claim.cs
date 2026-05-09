using JetBrains.Annotations;

namespace Blueprint.Authorization;

/// <summary>
/// Represents a demanded or granted claim consisting of a type, value, and value type.
/// This is the Blueprint-specific claim model used by <see cref="IClaimInspector"/> and
/// <see cref="ClaimRequiredAttribute"/> for permission evaluation, distinct from
/// <see cref="System.Security.Claims.Claim"/> to support hierarchical resource key matching.
/// </summary>
[PublicAPI]
public sealed class Claim
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Claim"/> class.
    /// </summary>
    /// <param name="type">The claim type URN (e.g. <see cref="Blueprint.Runtime.Authorization.ClaimTypes.Permission"/>).</param>
    /// <param name="value">The claim value, typically a resource key or wildcard.</param>
    /// <param name="valueType">The value type, typically the permission name (e.g. "Edit").</param>
    public Claim(string type, string value, string valueType)
    {
        this.Type = type;
        this.Value = value;
        this.ValueType = valueType;
    }

    /// <summary>
    /// Gets the claim type URN (e.g. "urn:claims/permission").
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// Gets the claim value — a resource key (e.g. "Tenant/123/User/456") or wildcard ("*").
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets the value type — typically the permission name (e.g. "ViewReport", "Edit").
    /// </summary>
    public string ValueType { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"[{this.Type}] {this.ValueType} = {this.Value}";
    }
}

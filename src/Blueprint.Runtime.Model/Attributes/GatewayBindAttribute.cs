namespace Blueprint.Model.Attributes;

/// <summary>
/// Binds, for every gateway operation in this assembly, the inner operation's property named
/// <see cref="PropertyName"/> from the caller's claims rather than from the request (D11's app tier). The property is left
/// out of each gateway operation's public shape, so a caller can neither see nor set it, and is converted to the inner
/// property's type by the rules route binding uses.
/// </summary>
/// <remarks>
/// <para>
/// For example <c>[assembly: GatewayBind("TenantId", FromClaim = "tenant_id")]</c> fills every exposed operation's
/// <c>TenantId</c> from the caller's <c>tenant_id</c> claim. A <see cref="BindAttribute"/> or <see cref="HideAttribute"/>
/// for the same property on one operation overrides it there.
/// </para>
/// <para>
/// A caller without the claim, or whose claim does not convert to the property's type, is refused with
/// 403 Forbidden before anything is sent upstream.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class GatewayBindAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GatewayBindAttribute"/> class.
    /// </summary>
    /// <param name="propertyName">The name of the inner operations' property to bind.</param>
    public GatewayBindAttribute(string propertyName)
    {
        this.PropertyName = propertyName;
    }

    /// <summary>
    /// Gets the name of the inner operations' property this binds.
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// Gets or sets the type of the caller's claim whose value the property is given. Required.
    /// </summary>
    public string? FromClaim { get; set; }
}

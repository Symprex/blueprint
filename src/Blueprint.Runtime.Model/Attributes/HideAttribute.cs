namespace Blueprint.Model.Attributes;

/// <summary>
/// Leaves one property of the inner operation a gateway operation forwards to out of the gateway operation's public
/// shape, and unset on the inner operation it sends (D11). The property cannot be <c>required</c> on the inner operation.
/// </summary>
/// <remarks>
/// It overrides an assembly's <see cref="GatewayBindAttribute"/> for the same property. The operation's <c>OnMap</c> or
/// <c>OnMapAsync</c> hook can still set it.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class HideAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HideAttribute"/> class.
    /// </summary>
    /// <param name="propertyName">The name of the inner operation's property to hide.</param>
    public HideAttribute(string propertyName)
    {
        this.PropertyName = propertyName;
    }

    /// <summary>
    /// Gets the name of the inner operation's property this hides.
    /// </summary>
    public string PropertyName { get; }
}

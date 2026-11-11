namespace Blueprint.Model.Attributes;

/// <summary>
/// Binds one property of the inner operation a gateway operation forwards to, in place of the value its caller sends
/// (D11's operation tier). The property is left out of the gateway operation's public shape. Set exactly one of
/// <see cref="FromClaim"/>, <see cref="FromRoute"/>, <see cref="FromProperty"/> or <see cref="Value"/>.
/// </summary>
/// <remarks>
/// A binding here overrides an assembly's <see cref="GatewayBindAttribute"/> for the same property. A bound value is set
/// after the gateway operation's own properties are copied and before the operation's <c>OnMap</c> or
/// <c>OnMapAsync</c> hook runs.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class BindAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BindAttribute"/> class.
    /// </summary>
    /// <param name="propertyName">The name of the inner operation's property to bind.</param>
    public BindAttribute(string propertyName)
    {
        this.PropertyName = propertyName;
    }

    /// <summary>
    /// Gets the name of the inner operation's property this binds.
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// Gets or sets the type of the caller's claim whose value the property is given, converted by the rules route
    /// binding uses. A caller without the claim is refused with 403 Forbidden.
    /// </summary>
    public string? FromClaim { get; set; }

    /// <summary>
    /// Gets or sets the gateway route placeholder whose value the property is given: the value of the gateway
    /// operation's property that the placeholder binds, which has to be of the inner property's type.
    /// </summary>
    public string? FromRoute { get; set; }

    /// <summary>
    /// Gets or sets the name of the gateway operation's property whose value the property is given, which has to be of
    /// the inner property's type.
    /// </summary>
    public string? FromProperty { get; set; }

    /// <summary>
    /// Gets or sets a fixed value for the property, of the inner property's type.
    /// </summary>
    public object? Value { get; set; }
}

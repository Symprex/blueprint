namespace Blueprint.Model.Attributes;

/// <summary>
/// Forwards a property of an operation marked <see cref="ForwardsToAttribute{TInner}"/> to the inner operation's property
/// of another name, so the public shape can rename it.
/// </summary>
/// <remarks>
/// The property takes the named inner property's validation rules and docs where it declares none of its own, and must
/// have its type. No other property of the operation may forward to the same inner property, and the inner property
/// cannot be one a binding fills.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ForwardAsAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ForwardAsAttribute"/> class.
    /// </summary>
    /// <param name="innerPropertyName">The name of the inner operation's property this one is forwarded to.</param>
    public ForwardAsAttribute(string innerPropertyName)
    {
        this.InnerPropertyName = innerPropertyName;
    }

    /// <summary>
    /// Gets the name of the inner operation's property this one is forwarded to.
    /// </summary>
    public string InnerPropertyName { get; }
}

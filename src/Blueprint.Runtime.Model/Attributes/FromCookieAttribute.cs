namespace Blueprint.Model.Attributes;

/// <summary>
/// Explicitly binds the property from an HTTP cookie. The cookie name is required
/// because cookie names do not follow C# naming conventions.
/// </summary>
/// <param name="name">The cookie name to bind from (e.g. <c>"session"</c>).</param>
[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class FromCookieAttribute(string name) : Attribute
{
    /// <summary>
    /// Gets the cookie name to bind from.
    /// </summary>
    public string Name { get; } = name;
}

namespace Blueprint.Model.Attributes;

/// <summary>
/// Explicitly binds the property from an HTTP request header. The header name is required
/// because header names do not follow C# naming conventions (e.g. <c>X-Tenant-Id</c>).
/// </summary>
/// <param name="name">The HTTP header name to bind from (e.g. <c>"X-Tenant-Id"</c>).</param>
[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class FromHeaderAttribute(string name) : Attribute
{
    /// <summary>
    /// Gets the HTTP header name to bind from.
    /// </summary>
    public string Name { get; } = name;
}

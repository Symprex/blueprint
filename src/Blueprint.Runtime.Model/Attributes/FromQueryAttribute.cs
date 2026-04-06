namespace Blueprint.Model.Attributes;

/// <summary>
/// Explicitly binds the property from the query string. By default the query parameter name
/// is the camelCase form of the property name; use <see cref="Name"/> to override.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class FromQueryAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the query string parameter name. When <c>null</c>, the camelCase property name is used.
    /// </summary>
    public string? Name { get; set; }
}

namespace Blueprint.Model.Attributes;

/// <summary>
/// Explicitly binds the property from the query string. By default the query parameter name
/// is the camelCase form of the property name; use <see cref="Name"/> to override.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class FromQueryAttribute : Attribute
{
    /// <summary>
    /// Initialises a new instance of the <see cref="FromQueryAttribute"/> class with
    /// the default property-name binding convention.
    /// </summary>
    public FromQueryAttribute()
    {
    }

    /// <summary>
    /// Initialises a new instance of the <see cref="FromQueryAttribute"/> class with
    /// an explicit query-string parameter name override.
    /// </summary>
    /// <param name="name">The query string parameter name to bind from.</param>
    public FromQueryAttribute(string name)
    {
        this.Name = name;
    }

    /// <summary>
    /// Gets or sets the query string parameter name. When <c>null</c>, the camelCase property name is used.
    /// </summary>
    public string? Name { get; set; }
}

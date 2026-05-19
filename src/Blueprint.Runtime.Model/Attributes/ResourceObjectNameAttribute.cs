namespace Blueprint.Model.Attributes;

/// <summary>
/// Overrides the generated <c>$object</c> type name for an API resource. By default, the
/// source generator derives the name from the class name by stripping common suffixes
/// (ApiResource, Resource, DTO) and camelCasing. Apply this attribute to use a custom name
/// instead.
/// </summary>
/// <remarks>
/// This is useful for gateway resources that project a backend resource under the same logical
/// type name (e.g. <c>GatewayImportRunApiResource</c> presenting as <c>"importRun"</c>).
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ResourceObjectNameAttribute : Attribute
{
    /// <summary>
    /// Initialises a new <see cref="ResourceObjectNameAttribute"/> with the explicit object name.
    /// </summary>
    /// <param name="name">The resource object name to use in serialized output (e.g. <c>"importRun"</c>).</param>
    public ResourceObjectNameAttribute(string name)
    {
        this.Name = name;
    }

    /// <summary>
    /// The resource object name to emit in the generated <c>Object</c> property override.
    /// </summary>
    public string Name { get; }
}

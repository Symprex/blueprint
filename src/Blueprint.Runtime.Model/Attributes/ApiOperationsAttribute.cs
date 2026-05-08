namespace Blueprint.Model.Attributes;

/// <summary>
/// Declares the explicit operation group that downstream descriptor and OpenAPI surfaces should use instead of the
/// namespace-derived fallback while the broader grouped-handler discovery work moves into core Blueprint.
/// </summary>
/// <param name="groupName">The stable grouped-operation name that later descriptor and OpenAPI surfaces should publish.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ApiOperationsAttribute(string groupName) : Attribute
{
    /// <summary>
    /// Gets the stable grouped-operation name that later descriptor and OpenAPI surfaces should publish.
    /// </summary>
    public string GroupName { get; } = groupName;

    /// <summary>
    /// Gets or sets an optional human-readable description for this operation group.
    /// When set, the description is published as the OpenAPI tag description and in the debug manifest.
    /// </summary>
    public string? Description { get; set; }
}

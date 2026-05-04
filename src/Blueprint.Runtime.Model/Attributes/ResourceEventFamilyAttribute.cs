namespace Blueprint.Model.Attributes;

/// <summary>
/// Declares a static partial class as a resource-event family that groups related event outcome types for a specific
/// resource type. The generator uses this to produce framework boilerplate, stable event IDs, and implicit conversions
/// so handlers can construct concrete event records without hand-authoring result plumbing.
/// </summary>
/// <param name="resourceType">The API resource type that body-bearing events in this family carry as their payload.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ResourceEventFamilyAttribute(Type resourceType) : Attribute
{
    /// <summary>
    /// Gets the API resource type that body-bearing events in this family carry as their payload.
    /// </summary>
    public Type ResourceType { get; } = resourceType;

    /// <summary>
    /// Gets or sets the stable resource-object token used as the prefix for generated event IDs.
    /// For example, <c>"template"</c> produces event IDs like <c>"template.created"</c>.
    /// This value forms part of the public wire contract and must not be derived from type names.
    /// </summary>
    public string ResourceObject { get; set; } = null!;

    /// <summary>
    /// Gets or sets the key type used to identify individual resource instances when a full resource body is not
    /// present (key-only events). Defaults to <see cref="string"/> when not specified.
    /// </summary>
    public Type? KeyType { get; set; }
}

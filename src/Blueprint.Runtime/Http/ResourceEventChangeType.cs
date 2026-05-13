using System.Text.Json.Serialization;

namespace Blueprint.Http;

/// <summary>
/// Classifies the coarse kind of resource mutation that a resource event represents.
/// This enum mirrors <see cref="Model.ChangeType"/> and exists for backward compatibility
/// with legacy Blueprint consumers that reference <c>ResourceEventChangeType</c> in the
/// <c>Blueprint.Http</c> namespace.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ResourceEventChangeType>))]
public enum ResourceEventChangeType
{
    /// <summary>
    /// The resource was newly created.
    /// </summary>
    Created,

    /// <summary>
    /// The resource was modified.
    /// </summary>
    Updated,

    /// <summary>
    /// The resource was removed.
    /// </summary>
    Deleted,
}

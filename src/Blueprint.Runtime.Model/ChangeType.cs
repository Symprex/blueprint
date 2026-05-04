namespace Blueprint.Model;

/// <summary>
/// Classifies the coarse kind of resource mutation that a resource event represents, providing a stable enumeration
/// for event families to declare whether the resource was created, updated, or deleted.
/// </summary>
public enum ChangeType
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

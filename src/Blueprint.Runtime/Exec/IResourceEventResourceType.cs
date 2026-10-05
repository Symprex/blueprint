namespace Blueprint.Runtime.Exec;

/// <summary>
/// A resource-event result that names the resource type its event belongs to without needing its content, so the event's
/// href can be made from that type's self link when the content could not be loaded.
/// </summary>
internal interface IResourceEventResourceType
{
    /// <summary>
    /// Gets the resource type the event belongs to.
    /// </summary>
    Type ResourceType { get; }
}

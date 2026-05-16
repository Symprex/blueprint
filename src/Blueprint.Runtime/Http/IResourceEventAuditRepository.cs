namespace Blueprint.Http;

/// <summary>
/// Provides persistence for resource event audit records. Implementations store the before/after
/// state of resources as they change through Blueprint operations, enabling audit trails and
/// change history.
/// </summary>
/// <remarks>
/// <para>
/// The typical flow is: before mutation, <see cref="GetCurrentDataAsync"/> is called to capture the
/// current state. After the handler executes successfully, <see cref="AddAsync"/> persists the
/// resource event which carries both the operation metadata and the before/after snapshots.
/// </para>
/// <para>
/// Implementations should persist independently of the ambient unit-of-work scope to ensure
/// audit records survive even when the main transaction is rolled back.
/// </para>
/// </remarks>
public interface IResourceEventAuditRepository
{
    /// <summary>
    /// Retrieves the current state of a resource identified by its canonical <paramref name="href"/>
    /// before a mutation is applied. This becomes the "before" snapshot in the resource event.
    /// </summary>
    /// <param name="href">The canonical self-link URL of the resource.</param>
    /// <param name="resourceType">The CLR type of the resource, used for deserialization hints.</param>
    /// <returns>The current state of the resource, or <c>null</c> if no prior state exists.</returns>
    Task<object?> GetCurrentDataAsync(string href, Type resourceType);

    /// <summary>
    /// Persists a resource event audit record containing the operation metadata, before/after state,
    /// and contextual information (user, timestamp, correlation IDs).
    /// </summary>
    /// <param name="resourceEvent">The resource event to persist.</param>
    /// <returns>A task that completes when the event has been durably stored.</returns>
    Task AddAsync(ResourceEvent resourceEvent);
}

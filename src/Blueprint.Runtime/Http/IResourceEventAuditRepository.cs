namespace Blueprint.Http;

/// <summary>
/// Provides persistence for resource event audit records. Implementations store the before/after
/// state of resources as they change through Blueprint operations, enabling audit trails and
/// change history.
/// </summary>
/// <remarks>
/// <para>
/// The generated pipeline calls <see cref="AddAsync"/> for every legacy <see cref="ResourceEvent"/> an operation raises,
/// unless the operation's type carries <c>[DoNotAudit]</c>, once the event is stamped, has its href and has its data
/// expanded, as legacy Blueprint's <c>ResourceEventHandler</c> did. It does not call <see cref="GetCurrentDataAsync"/>,
/// which legacy called for an updated event to fill its changed values.
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

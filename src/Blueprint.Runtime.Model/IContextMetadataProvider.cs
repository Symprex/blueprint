using JetBrains.Annotations;

namespace Blueprint.Model;

/// <summary>
/// Provides contextual metadata (such as tenant ID, user ID, correlation ID) that is
/// automatically applied to resource events and audit records. Implementations are
/// resolved from DI and called during resource event creation to enrich events with
/// ambient context data.
/// </summary>
/// <remarks>
/// <para>
/// Multiple providers may be registered. Each is called in order and all key-value pairs
/// are merged into the event metadata. This enables separation of concerns: one provider
/// for tenant context, another for correlation IDs, etc.
/// </para>
/// <para>
/// Providers should be lightweight and avoid I/O — they are called synchronously on every
/// resource event.
/// </para>
/// </remarks>
[PublicAPI]
public interface IContextMetadataProvider
{
    /// <summary>
    /// Populates metadata entries that should be associated with the current operation context.
    /// </summary>
    /// <param name="add">
    /// A callback to add key-value metadata pairs. Keys should use dot-notation namespacing
    /// (e.g. "tenant.id", "correlation.id", "user.email").
    /// </param>
    void PopulateMetadata(Action<string, string> add);

    /// <summary>
    /// Populates metadata entries, passing through a <typeparamref name="TState"/> parameter so
    /// the callback can be a <c>static</c> delegate (zero closure allocation).
    /// </summary>
    /// <typeparam name="TState">The type of the state passed through to the callback.</typeparam>
    /// <param name="add">A static callback that receives key, value, and state.</param>
    /// <param name="state">The state to pass through to each callback invocation.</param>
    void PopulateMetadata<TState>(Action<string, string, TState> add, TState state)
    {
        this.PopulateMetadata((key, value) => add(key, value, state));
    }
}

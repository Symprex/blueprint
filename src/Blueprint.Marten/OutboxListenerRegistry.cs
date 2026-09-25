using System.Runtime.CompilerServices;
using Marten;

namespace Blueprint.Marten;

/// <summary>
/// Remembers the drain listeners added through <see cref="MartenServiceCollectionExtensions.AddBlueprintOutboxListener"/>,
/// so that <see cref="MartenUnitOfWork"/> can make sure Marten runs them on every save.
/// </summary>
/// <remarks>
/// Keyed by the listener instance, and weakly, so that a listener whose store is gone is not kept alive by it.
/// </remarks>
internal static class OutboxListenerRegistry
{
    private static readonly object _marker = new();
    private static readonly ConditionalWeakTable<IDocumentSessionListener, object> _listeners = new();

    /// <summary>
    /// Records <paramref name="listener"/> as an outbox drain.
    /// </summary>
    /// <param name="listener">The listener added as an outbox drain.</param>
    public static void Register(IDocumentSessionListener listener)
    {
        _listeners.AddOrUpdate(listener, _marker);
    }

    /// <summary>
    /// Gets whether any of <paramref name="listeners"/> was added as an outbox drain.
    /// </summary>
    /// <param name="listeners">A session's listeners.</param>
    /// <returns><see langword="true"/> when at least one of them is an outbox drain.</returns>
    public static bool ContainsAny(IEnumerable<IDocumentSessionListener> listeners)
    {
        return listeners.Any(static listener => _listeners.TryGetValue(listener, out _));
    }
}

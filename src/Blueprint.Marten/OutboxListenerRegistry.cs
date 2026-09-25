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
    /// Gets whether one of <paramref name="session"/>'s listeners is an outbox drain that may have something to write: one
    /// that is not an <see cref="IOutboxDrainListener"/>, so cannot say, or one that says it has messages.
    /// </summary>
    /// <param name="session">The session about to be saved with nothing else to save.</param>
    /// <returns><see langword="true"/> when a drain must be run for the save.</returns>
    public static bool HasDrainToRun(IDocumentSession session)
    {
        foreach (var listener in session.Listeners)
        {
            if (!_listeners.TryGetValue(listener, out _))
            {
                continue;
            }

            if (listener is not IOutboxDrainListener drain || drain.HasPendingMessages(session))
            {
                return true;
            }
        }

        return false;
    }
}

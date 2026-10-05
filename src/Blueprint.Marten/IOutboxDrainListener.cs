using Marten;

namespace Blueprint.Marten;

/// <summary>
/// An outbox drain, added with <see cref="MartenServiceCollectionExtensions.AddBlueprintOutboxListener"/>, that can say
/// whether a save has anything for it to write, so that <see cref="MartenUnitOfWork"/> opens a connection for a session
/// with nothing else to save only when it has.
/// </summary>
/// <remarks>
/// <para>
/// Marten 6 runs no session listener for a session with nothing to save, so <see cref="MartenUnitOfWork"/> makes Marten
/// run the drains of such a session by queuing a statement that does nothing. For a drain that is not an
/// <see cref="IOutboxDrainListener"/> it does so on every such save, which costs a round trip, and a database that is down
/// then fails a save that had nothing to write. A drain that implements this is only run that way when
/// <see cref="HasPendingMessages"/> says it has messages; otherwise the save does nothing, touching no database, as legacy
/// Blueprint's did (Signature365 <c>8b40ecc961</c>: its Rebus commit queued the outbox rows on the session before the save,
/// so the session had work exactly when a message had been sent, <c>AzureAdOauthReturnCommandExecutorPipeline.cs:133-137</c>).
/// </para>
/// <para>
/// A session that has something else to save runs every listener anyway, so this is not asked then.
/// </para>
/// </remarks>
public interface IOutboxDrainListener : IDocumentSessionListener
{
    /// <summary>
    /// Gets whether, were <paramref name="session"/> saved now, this drain would write anything into it: the messages sent
    /// so far in the current Rebus transaction and not yet drained, for a drain of the ambient transaction's outbox.
    /// </summary>
    /// <remarks>
    /// Called before the save, outside the listener chain, so it must not change the session. Answering
    /// <see langword="true"/> when in doubt costs a round trip; answering <see langword="false"/> wrongly loses messages.
    /// </remarks>
    /// <param name="session">The session about to be saved.</param>
    /// <returns><see langword="true"/> when the drain has something to write.</returns>
    bool HasPendingMessages(IDocumentSession session);
}

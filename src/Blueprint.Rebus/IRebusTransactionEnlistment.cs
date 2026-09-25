using JetBrains.Annotations;
using Rebus.Transport;

namespace Blueprint.Rebus;

/// <summary>
/// Enlists a consumer's resources in a Rebus transaction that a unit of work will be saved inside, before any handler
/// runs in it — typically to make the unit of work's store the transaction's outbox, so that the messages sent in the
/// transaction are written in the same database transaction as the data.
/// </summary>
/// <remarks>
/// <para>
/// Blueprint calls every registered enlistment, in registration order, through
/// <see cref="RebusTransactionEnlistment.EnlistAsync"/>, in exactly the two places where a Rebus transaction and a unit of
/// work save meet:
/// </para>
/// <list type="bullet">
/// <item><description>
/// a generated command or background-task pipeline that saves a unit of work, with the transaction of the
/// <c>RebusTransactionScope</c> it opens, and only when an <see cref="Persistence.IUnitOfWork"/> is registered. It
/// enlists once the tenant is resolved and the operation authorised and validated, straight before the handler, as
/// legacy did, so an enlistment resolving a tenant-scoped service gets the caller's tenant, and an operation that is
/// refused enlists nothing. The pipeline saves the unit of work before it completes the scope;
/// </description></item>
/// <item><description>
/// <see cref="BlueprintApiActivator"/>, for a received message handled by plain <c>IHandleMessages&lt;T&gt;</c>, with the
/// received message's transaction, when an <see cref="Persistence.IUnitOfWork"/> is registered. The unit of work is saved
/// from that transaction's first commit callback, which runs before any commit callback an enlistment or a handler
/// registers, and with that transaction as <see cref="AmbientTransactionContext.Current"/>.
/// </description></item>
/// </list>
/// <para>
/// In both, the unit of work is saved after the handlers have returned and before Rebus sends anything, so every message
/// the handlers sent is in the transaction when the save runs. An enlistment that has those messages written into the
/// unit of work during the save — a Marten session listener added with <c>AddBlueprintOutboxListener</c>, for example —
/// commits them atomically with the data.
/// </para>
/// <para>
/// The save is not the last point at which a message can join the transaction, though:
/// </para>
/// <list type="bullet">
/// <item><description>
/// a generated pipeline commits the <see cref="Messaging.IMessageOutbox"/>, runs the audit sink and flushes the
/// background tasks after the save and before it completes the scope, so a message any of them sends joins the
/// transaction after the save. Legacy had no such window: it completed its scope before it saved its session, and
/// flushed and audited after it had completed the scope, which was still ambient until the method returned, so a send
/// there was refused rather than joining it (legacy <c>AzureAdOauthReturnCommandExecutorPipeline.cs:117</c>,
/// <c>:133-149</c> at Signature365 <c>8b40ecc961</c>; Rebus 8.6's <c>TransactionContext</c> throws, through
/// <c>ThrowCompletedException</c>, when a completed context is given an action, as a transport's send gives it one);
/// </description></item>
/// <item><description>
/// for a received message, a commit callback that runs after the save — one an enlistment or a handler registered —
/// runs after it, and a message it sends joins the transaction after the save too.
/// </description></item>
/// </list>
/// <para>
/// Such a message is not in the saved unit of work. An enlistment must not let it be queued into a unit of work that has
/// already saved, where nothing would ever write it: it must refuse it, or send it without the outbox.
/// </para>
/// <para>
/// Blueprint checks for that mistake. Once a transaction it enlisted has run its commit callbacks, it asks the scope's
/// unit of work, when that is an <see cref="Persistence.IUnitOfWorkSaveGuard"/> (Blueprint.Marten's is), whether work was
/// queued on it since its save, and fails the transaction's completion if so (see
/// <see cref="RebusTransactionEnlistment.EnlistAsync"/>).
/// </para>
/// <para>
/// Register implementations in DI; they are resolved from the operation's or message's own scope.
/// </para>
/// </remarks>
[PublicAPI]
public interface IRebusTransactionEnlistment
{
    /// <summary>
    /// Enlists in <paramref name="transactionContext"/>, before any handler runs in it.
    /// </summary>
    /// <param name="transactionContext">The Rebus transaction the unit of work will be saved inside.</param>
    /// <param name="services">The scoped service provider of the operation or message being handled.</param>
    /// <param name="cancellationToken">The cancellation token of the operation or delivery.</param>
    /// <returns>A task that completes when the enlistment is done.</returns>
    ValueTask EnlistAsync(ITransactionContext transactionContext, IServiceProvider services, CancellationToken cancellationToken);
}

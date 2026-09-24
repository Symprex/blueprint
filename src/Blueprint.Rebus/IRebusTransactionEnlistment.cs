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
/// In both, the save happens after every message has been sent and before Rebus sends anything, so an enlistment that
/// has the outbox's messages written into the unit of work during the save — a Marten session listener registered with
/// <c>AddBlueprintOutboxListener</c>, for example — commits data and messages atomically. An enlistment must not let
/// messages be queued into a unit of work that has already saved: they would never be written.
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

using Blueprint.Persistence;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Transport;

namespace Blueprint.Rebus;

/// <summary>
/// Runs every registered <see cref="IRebusTransactionEnlistment"/> for a Rebus transaction. Called by generated pipelines
/// and by <see cref="BlueprintApiActivator"/>; see <see cref="IRebusTransactionEnlistment"/> for when.
/// </summary>
[PublicAPI]
public static class RebusTransactionEnlistment
{
    /// <summary>
    /// Calls every <see cref="IRebusTransactionEnlistment"/> registered in <paramref name="services"/>, in registration
    /// order, with <paramref name="transactionContext"/>. Does nothing when none is registered, or when
    /// <paramref name="services"/> resolves no enumerable of them at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When at least one enlistment ran, it also registers an ack callback that, once the transaction has run its commit
    /// callbacks, calls <see cref="IUnitOfWorkSaveGuard.ThrowIfChangedSinceSave"/> on the scope's unit of work, when that
    /// is one. An outbox that queued work on the unit of work from a commit callback queued it after the save, where it is
    /// never written, and this makes completing the transaction fail instead.
    /// </para>
    /// <para>
    /// It is an ack callback because Rebus runs a transaction's ack callbacks after all of its commit callbacks, including
    /// those registered after this one, whereas a commit callback registered while the commit callbacks run is not run by
    /// that completion (<c>RebusTransactionCallbackOrderTests</c>). For a generated pipeline the failure fails the
    /// operation. For a received message the transport has already acknowledged the message by the time it runs, so the
    /// failure is reported and logged by Rebus, and the message is not redelivered.
    /// </para>
    /// </remarks>
    /// <param name="transactionContext">The Rebus transaction a unit of work will be saved inside.</param>
    /// <param name="services">The scoped service provider of the operation or message being handled.</param>
    /// <param name="cancellationToken">The cancellation token of the operation or delivery.</param>
    /// <returns>A task that completes when every enlistment has run.</returns>
    public static async ValueTask EnlistAsync(
        ITransactionContext transactionContext,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transactionContext);
        ArgumentNullException.ThrowIfNull(services);

        // Not GetServices, which throws for a provider that resolves no enumerable: a generated pipeline enlists whenever
        // it cannot rule out a unit of work, which with a provider other than Microsoft's container is always.
        var enlistments = services.GetService<IEnumerable<IRebusTransactionEnlistment>>();

        if (enlistments is null)
        {
            return;
        }

        var enlisted = false;

        foreach (var enlistment in enlistments)
        {
            await enlistment.EnlistAsync(transactionContext, services, cancellationToken).ConfigureAwait(false);

            enlisted = true;
        }

        if (enlisted)
        {
            transactionContext.OnAck(_ => ThrowIfUnitOfWorkChangedSinceSave(services));
        }
    }

    private static Task ThrowIfUnitOfWorkChangedSinceSave(IServiceProvider services)
    {
        (services.GetService<IUnitOfWork>() as IUnitOfWorkSaveGuard)?.ThrowIfChangedSinceSave();

        return Task.CompletedTask;
    }
}

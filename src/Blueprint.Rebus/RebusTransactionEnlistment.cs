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

        foreach (var enlistment in enlistments)
        {
            await enlistment.EnlistAsync(transactionContext, services, cancellationToken).ConfigureAwait(false);
        }
    }
}

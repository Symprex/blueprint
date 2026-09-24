using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Transport;

namespace Blueprint.Rebus.Tests;

/// <summary>
/// Proves that <see cref="RebusTransactionEnlistment.EnlistAsync"/> calls every registered
/// <see cref="IRebusTransactionEnlistment"/> in registration order, and does nothing when none can be resolved.
/// </summary>
public class RebusTransactionEnlistmentTests
{
    [Fact]
    public async Task EnlistAsync_calls_every_enlistment_in_registration_order_with_the_transaction_and_services()
    {
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<IRebusTransactionEnlistment>(new RecordingEnlistment("first", log));
        services.AddSingleton<IRebusTransactionEnlistment>(new RecordingEnlistment("second", log));
        await using var provider = services.BuildServiceProvider();
        using var scope = new RebusTransactionScope();

        await RebusTransactionEnlistment.EnlistAsync(scope.TransactionContext, provider, CancellationToken.None);

        log.Should().Equal("first", "second");
        provider.GetServices<IRebusTransactionEnlistment>().Cast<RecordingEnlistment>()
            .Should().AllSatisfy(enlistment =>
            {
                enlistment.TransactionContext.Should().BeSameAs(scope.TransactionContext);
                enlistment.Services.Should().BeSameAs(provider);
            });
    }

    [Fact]
    public async Task EnlistAsync_with_a_provider_that_resolves_no_enlistments_does_nothing()
    {
        // A generated pipeline enlists whenever it cannot rule out a unit of work, which with a provider that is not the
        // Microsoft container (no IServiceProviderIsService) is always; such a provider may resolve no enumerable at all.
        using var scope = new RebusTransactionScope();

        var enlist = async () => await RebusTransactionEnlistment.EnlistAsync(scope.TransactionContext, new EmptyServiceProvider(), CancellationToken.None);

        await enlist.Should().NotThrowAsync();
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }

    private sealed class RecordingEnlistment(string name, List<string> log) : IRebusTransactionEnlistment
    {
        public ITransactionContext? TransactionContext { get; private set; }

        public IServiceProvider? Services { get; private set; }

        public ValueTask EnlistAsync(ITransactionContext transactionContext, IServiceProvider services, CancellationToken cancellationToken)
        {
            this.TransactionContext = transactionContext;
            this.Services = services;
            log.Add(name);

            return ValueTask.CompletedTask;
        }
    }
}

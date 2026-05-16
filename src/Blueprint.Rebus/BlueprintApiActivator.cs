using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Activation;
using Rebus.Bus;
using Rebus.Handlers;
using Rebus.Pipeline;
using Rebus.Transport;

namespace Blueprint.Rebus;

/// <summary>
/// A <see cref="IHandlerActivator" /> that enables integration of a Blueprint pipeline with
/// Rebus, in that any operations that are registered in the given <see cref="BlueprintAppExecutor" />
/// will be executed through the generated pipeline when received as Rebus messages.
/// </summary>
/// <remarks>
/// <para>
/// Each message type is resolved to a <see cref="BlueprintApp.FindOperation"/> descriptor once
/// and then cached. A fresh DI scope is created per-message to match the Blueprint execution model.
/// </para>
/// </remarks>
public sealed class BlueprintApiActivator : IContainerAdapter
{
    private readonly ConcurrentDictionary<Type, object> _typesToHandler = new();
    private readonly IServiceProvider _serviceProvider;
    private readonly BlueprintApp _app;

    /// <summary>
    /// Initializes a new instance of the <see cref="BlueprintApiActivator"/> class.
    /// </summary>
    /// <param name="serviceProvider">The root service provider used for scope creation.</param>
    public BlueprintApiActivator(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        this._serviceProvider = serviceProvider;
        this._app = serviceProvider.GetRequiredService<BlueprintApp>();
    }

    /// <inheritdoc/>
    public Task<IEnumerable<IHandleMessages<TMessage>>> GetHandlers<TMessage>(
        TMessage message,
        ITransactionContext transactionContext)
    {
        if (this._app.TryFindOperation(typeof(TMessage), out var descriptor))
        {
            var desc = descriptor;

            // Blueprint operation — use cached pipeline handler
            return (Task<IEnumerable<IHandleMessages<TMessage>>>)this._typesToHandler.GetOrAdd(
                typeof(TMessage),
                static (key, state) =>
                {
                    // Tag the descriptor as a Rebus message and hide from HTTP exposure
                    if (!state.desc.TryGetFeatureData<RebusMessageFeature>(out _))
                    {
                        state.desc.SetFeatureData(new RebusMessageFeature());
                        state.desc.IsExposed = false;
                        state.desc.AnonymousAccessAllowed = true;
                    }

                    var singleHandler = (IHandleMessages<TMessage>)Activator
                        .CreateInstance(
                            typeof(BlueprintHandler<>).MakeGenericType(key),
                            state.sp)!;

                    return Task.FromResult<IEnumerable<IHandleMessages<TMessage>>>([singleHandler]);
                },
                (sp: this._serviceProvider, desc));
        }

        // Not a Blueprint operation — resolve IHandleMessages<T> from a fresh DI scope.
        // Each Rebus message dispatch creates a new scope for handler lifetime management.
        using var scope = this._serviceProvider.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IHandleMessages<TMessage>>().ToList();

        return Task.FromResult<IEnumerable<IHandleMessages<TMessage>>>(handlers);
    }

    /// <inheritdoc/>
    public void SetBus(IBus bus)
    {
        var config = this._serviceProvider.GetRequiredService<BlueprintRebusConfiguration>();

        Task.WaitAll(config.SubscribedMessages.Select(bus.Subscribe).ToArray());
    }

#pragma warning disable CA1812 // Instantiated via Activator.CreateInstance
    private sealed class BlueprintHandler<T> : IHandleMessages<T>
#pragma warning restore CA1812
    {
        private readonly IServiceProvider _serviceProvider;

        public BlueprintHandler(IServiceProvider serviceProvider)
        {
            this._serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Handles a Rebus message by creating a scoped Blueprint execution context and
        /// running the message through the generated pipeline. Unless the operation type
        /// is decorated with <see cref="NoRebusTransactionAttribute"/>, execution is wrapped
        /// in a <see cref="RebusTransactionScope"/> so that outgoing messages are batched
        /// and sent only after successful completion.
        /// </summary>
        public async Task Handle(T message)
        {
            var messageContext = MessageContext.Current;
            var cancellationToken = messageContext.IncomingStepContext.Load<CancellationToken>();

            using var serviceScope = this._serviceProvider.CreateScope();
            var services = serviceScope.ServiceProvider;

            var executor = services.GetRequiredService<BlueprintAppExecutor>();
            var descriptor = executor.App.FindOperation(typeof(T));

            var apiContext = new ApiOperationContext(
                services,
                executor.App,
                descriptor,
                cancellationToken)
            {
                // Replace the default-constructed operation with the actual message instance
                Operation = message!,
            };

            // Store the Rebus message context so pipeline code can access it
            apiContext.Data["MessageContext"] = messageContext;

            // Wrap in Rebus transaction scope unless opted out via [NoRebusTransaction]
            var useTransaction = !typeof(T).IsDefined(typeof(NoRebusTransactionAttribute), inherit: false);

            if (useTransaction)
            {
                using var txnScope = new RebusTransactionScope();

                var result = await executor.ExecuteAsync(apiContext);

                if (result is UnhandledExceptionResult { Exception: { } exception })
                {
                    ExceptionDispatchInfo.Throw(exception);
                }

                await txnScope.CompleteAsync();
            }
            else
            {
                var result = await executor.ExecuteAsync(apiContext);

                if (result is UnhandledExceptionResult { Exception: { } exception })
                {
                    ExceptionDispatchInfo.Throw(exception);
                }
            }
        }
    }
}

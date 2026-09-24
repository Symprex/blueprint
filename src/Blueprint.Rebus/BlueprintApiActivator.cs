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
/// <para>
/// For an operation, that scope lives inside the cached handler's <c>Handle</c>. For any other message, the
/// <see cref="IHandleMessages{TMessage}"/> registered in DI are resolved from a scope owned by the Rebus
/// transaction context, because Rebus calls <c>Handle</c> after <see cref="GetHandlers{TMessage}"/> has returned.
/// That scope, and every scoped dependency of those handlers, is disposed when Rebus disposes the transaction
/// context: after the handlers, and after whichever commit or rollback and ack or nack callbacks Rebus runs for
/// the outcome, whether the message succeeded or not.
/// </para>
/// <para>
/// The <see cref="Persistence.IUnitOfWork"/> of that scope, when one is registered, is saved when Rebus commits the
/// transaction, so the writes those handlers made through it are persisted exactly when the delivery succeeds.
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

        return this.GetPlainHandlersAsync<TMessage>(transactionContext);
    }

    /// <summary>
    /// Resolves the plain <see cref="IHandleMessages{TMessage}"/> for a message that is not a Blueprint operation, from a
    /// fresh DI scope owned by the message's transaction.
    /// </summary>
    /// <remarks>
    /// The order in which this registers with the transaction is what an outbox depends on: the scope's disposal first,
    /// so nothing that follows can leak it; then the unit of work save as the first commit callback; then the consumer's
    /// <see cref="IRebusTransactionEnlistment"/>s, so any commit callback they add runs after the save; and all of it
    /// before a handler runs, so before a handler first sends.
    /// </remarks>
    private async Task<IEnumerable<IHandleMessages<TMessage>>> GetPlainHandlersAsync<TMessage>(ITransactionContext transactionContext)
    {
        // Rebus calls Handle only after this method has returned, so the scope cannot end here: the transaction context
        // owns it, and it ends when Rebus disposes that context, after the handlers and the callbacks for the outcome have
        // run.
        var scope = this._serviceProvider.CreateAsyncScope();

        // Registered before resolving, so a handler or enlistment that fails cannot leak what was already resolved.
        transactionContext.OnDisposed(_ => DisposeHandlerScope(scope));

        // Registered before any handler runs, so it precedes the commit callback the transport registers when a handler
        // first sends: the handlers' writes are persisted before the messages announcing them go out.
        var cancellationToken = MessageContext.Current?.IncomingStepContext.Load<CancellationToken>() ?? CancellationToken.None;
        transactionContext.OnCommit(_ => SaveHandlerUnitOfWorkAsync(scope.ServiceProvider, cancellationToken));

        // Only when a unit of work will be saved in this transaction: an outbox enlisted where nothing is saved would
        // never be written.
        if (IsUnitOfWorkRegistered(scope.ServiceProvider))
        {
            await RebusTransactionEnlistment.EnlistAsync(transactionContext, scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
        }

        return scope.ServiceProvider.GetServices<IHandleMessages<TMessage>>().ToList();
    }

    private static bool IsUnitOfWorkRegistered(IServiceProvider services)
    {
        return services.GetService<IServiceProviderIsService>() is { } isService
            ? isService.IsService(typeof(Persistence.IUnitOfWork))
            : services.GetService<Persistence.IUnitOfWork>() is not null;
    }

    /// <inheritdoc/>
    public void SetBus(IBus bus)
    {
        var config = this._serviceProvider.GetRequiredService<BlueprintRebusConfiguration>();

        Task.WaitAll(config.SubscribedMessages.Select(bus.Subscribe).ToArray());
    }

    /// <summary>
    /// Ends a plain handler's DI scope from <see cref="ITransactionContext.OnDisposed"/>. That is the only transaction
    /// callback Rebus invokes on every path — a context disposed without a result runs neither its commit nor its ack
    /// callbacks — and it is synchronous, so the asynchronous disposal is waited for here.
    /// </summary>
    /// <remarks>
    /// The scope is disposed asynchronously because a synchronous dispose throws on a scoped service that implements
    /// only <see cref="IAsyncDisposable"/>, leaving it and everything resolved before it undisposed. It runs on the
    /// thread pool so that waiting for it cannot deadlock on a synchronisation context held by whoever disposes the
    /// transaction context, and it completes before that disposal returns. Rebus reports an exception thrown here
    /// through the transaction context's error handling rather than to the handler.
    /// </remarks>
    /// <param name="scope">The scope the message's handlers were resolved from.</param>
    private static void DisposeHandlerScope(AsyncServiceScope scope)
    {
        Task.Run(() => scope.DisposeAsync().AsTask()).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Saves the <see cref="Persistence.IUnitOfWork"/> of a plain handler's DI scope, if one is registered, from
    /// <see cref="ITransactionContext.OnCommit"/>. That is the point at which Rebus has decided the message succeeded:
    /// every handler has returned, and a delivery that failed or was aborted runs no commit callbacks, so nothing is
    /// saved for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Legacy Blueprint ran every subscribed message as an operation whose pipeline saved the unit of work after the
    /// handler, so plain handlers write through an injected session and rely on that save. An operation message is
    /// not saved here: its generated pipeline already saves its own unit of work.
    /// </para>
    /// <para>
    /// The unit of work is resolved from the handlers' own scope, so it is the one they wrote through. A save that
    /// throws fails the transaction's completion, so Rebus treats the delivery as failed and redelivers it.
    /// </para>
    /// <para>
    /// It is a commit callback, not an ack callback, because Rebus acks a delivery it has given up on and moved to the
    /// error queue without committing it. Saving on ack would persist the writes of a handler that threw.
    /// </para>
    /// </remarks>
    /// <param name="services">The service provider of the scope the message's handlers were resolved from.</param>
    /// <param name="cancellationToken">The cancellation token of the delivery.</param>
    /// <returns>A task that completes when the unit of work has been saved, or at once when none is registered.</returns>
    private static Task SaveHandlerUnitOfWorkAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var unitOfWork = services.GetService<Persistence.IUnitOfWork>();

        return unitOfWork is not null
            ? unitOfWork.SaveChangesAsync(cancellationToken)
            : Task.CompletedTask;
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

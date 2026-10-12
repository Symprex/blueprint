using Rebus.Bus;
using Rebus.Bus.Advanced;
using Rebus.DataBus;
using Rebus.Transport;

namespace Blueprint.Rebus;

/// <summary>
/// The <see cref="IBus"/> that <c>AddBlueprintRebus</c> registers, which is exactly the bus it wraps except that deferring or
/// forwarding the message a Blueprint pipeline is handling is done in that message's own transaction.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ITransportMessageApi.Defer"/> and <see cref="ITransportMessageApi.Forward"/> find the message being handled, and
/// the transaction to send its copy in, through the ambient transaction context. Around an operation delivered as a message,
/// <see cref="BlueprintApiActivator"/> and then the generated pipeline replace that with a
/// <see cref="RebusTransactionScope"/> of their own, which has no message in it, so inside the handler both would throw a
/// <see cref="KeyNotFoundException"/> for <c>stepContext</c>. The context of the incoming message, captured by the activator
/// (<see cref="IncomingMessageContext"/>), is made the ambient one for the call instead.
/// </para>
/// <para>
/// That also decides which transaction the copy is sent in: the incoming message's, not the pipeline's. So it is sent only once
/// the message has been handled and its delivery succeeds, is discarded if the handler throws, and does not go through an
/// outbox enlisted in the pipeline's scope, which needs the database that may well be why the message is being deferred.
/// </para>
/// <para>
/// Everything else, publishing and sending included, goes straight to the wrapped bus in whatever transaction is ambient,
/// which inside a handler is the pipeline's, so it goes out with the pipeline's unit of work and through its outbox, and is
/// correlated as it was before. Where no message is being handled through a pipeline — a plain
/// <c>IHandleMessages&lt;T&gt;</c>, which runs in its message's own transaction, an HTTP request, a background job — the
/// transport message API is the wrapped bus's too, so Rebus's own error is what a call there meets.
/// </para>
/// </remarks>
internal sealed class IncomingMessageAwareBus : IBus
{
    private readonly IBus _bus;

    /// <summary>
    /// Initializes a new instance of the <see cref="IncomingMessageAwareBus"/> class.
    /// </summary>
    /// <param name="bus">The bus to wrap, which this one disposes when it is disposed.</param>
    public IncomingMessageAwareBus(IBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);

        this._bus = bus;
    }

    /// <inheritdoc/>
    public IAdvancedApi Advanced => new AdvancedApi(this._bus.Advanced);

    /// <inheritdoc/>
    public Task SendLocal(object commandMessage, IDictionary<string, string>? optionalHeaders = null)
    {
        return this._bus.SendLocal(commandMessage, optionalHeaders);
    }

    /// <inheritdoc/>
    public Task Send(object commandMessage, IDictionary<string, string>? optionalHeaders = null)
    {
        return this._bus.Send(commandMessage, optionalHeaders);
    }

    /// <inheritdoc/>
    public Task DeferLocal(TimeSpan delay, object message, IDictionary<string, string>? optionalHeaders = null)
    {
        return this._bus.DeferLocal(delay, message, optionalHeaders);
    }

    /// <inheritdoc/>
    public Task Defer(TimeSpan delay, object message, IDictionary<string, string>? optionalHeaders = null)
    {
        return this._bus.Defer(delay, message, optionalHeaders);
    }

    /// <inheritdoc/>
    public Task Reply(object replyMessage, IDictionary<string, string>? optionalHeaders = null)
    {
        return this._bus.Reply(replyMessage, optionalHeaders);
    }

    /// <inheritdoc/>
    public Task Subscribe<TEvent>()
    {
        return this._bus.Subscribe<TEvent>();
    }

    /// <inheritdoc/>
    public Task Subscribe(Type eventType)
    {
        return this._bus.Subscribe(eventType);
    }

    /// <inheritdoc/>
    public Task Unsubscribe<TEvent>()
    {
        return this._bus.Unsubscribe<TEvent>();
    }

    /// <inheritdoc/>
    public Task Unsubscribe(Type eventType)
    {
        return this._bus.Unsubscribe(eventType);
    }

    /// <inheritdoc/>
    public Task Publish(object eventMessage, IDictionary<string, string>? optionalHeaders = null)
    {
        return this._bus.Publish(eventMessage, optionalHeaders);
    }

    /// <summary>
    /// Disposes the wrapped bus, which stops its workers. The container that registered this bus disposes it, and not the
    /// bus it wraps, so this must pass on.
    /// </summary>
    public void Dispose()
    {
        this._bus.Dispose();
    }

    private sealed class AdvancedApi(IAdvancedApi advanced) : IAdvancedApi
    {
        public IWorkersApi Workers => advanced.Workers;

        public ITopicsApi Topics => advanced.Topics;

        public IRoutingApi Routing => advanced.Routing;

        public ITransportMessageApi TransportMessage => new TransportMessageApi(advanced.TransportMessage);

        public IDataBus DataBus => advanced.DataBus;

        public ISyncBus SyncBus => advanced.SyncBus;
    }

    private sealed class TransportMessageApi(ITransportMessageApi transportMessage) : ITransportMessageApi
    {
        public Task Forward(string destinationAddress, Dictionary<string, string>? optionalAdditionalHeaders = null)
        {
            return InIncomingMessageTransactionAsync(() => transportMessage.Forward(destinationAddress, optionalAdditionalHeaders));
        }

        public Task Defer(TimeSpan delay, Dictionary<string, string>? optionalAdditionalHeaders = null)
        {
            return InIncomingMessageTransactionAsync(() => transportMessage.Defer(delay, optionalAdditionalHeaders));
        }

        private static async Task InIncomingMessageTransactionAsync(Func<Task> operation)
        {
            // Not handling a message through a pipeline, so whatever is ambient is left alone and Rebus says what is wrong
            if (IncomingMessageContext.Current is not { } incoming)
            {
                await operation().ConfigureAwait(false);

                return;
            }

            // This is an async method, so the change to the ambient context cannot reach the caller; it is put back anyway,
            // so nothing that runs on in this flow after the call is left in the message's transaction
            var previous = AmbientTransactionContext.Current;

            AmbientTransactionContext.SetCurrent(incoming.TransactionContext);

            try
            {
                await operation().ConfigureAwait(false);
            }
            finally
            {
                AmbientTransactionContext.SetCurrent(previous);
            }
        }
    }
}

using Rebus.Pipeline;
using Rebus.Transport;

namespace Blueprint.Rebus;

/// <summary>
/// The transaction in which a plain handler's copy of the message it is handling — deferred or forwarded — is sent, when an outbox
/// is enlisted in that message's own transaction: a transaction of its own, completed when the message's commits and discarded
/// when the message's ends without committing.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="BlueprintApiActivator"/> enlists a plain <c>IHandleMessages&lt;T&gt;</c>'s message's own transaction in the
/// consumer's <see cref="IRebusTransactionEnlistment"/>s, typically an outbox, whose transport holds whatever is sent in that
/// transaction until the unit of work saves it. Rebus sends a deferred or forwarded copy in the ambient transaction, so the copy
/// would be held too, and saving it needs the database, which may well be why the message is being deferred. Legacy Blueprint
/// enlisted the outbox in a pipeline scope of its own, so the copy went out in the message's transaction, past it (Signature365
/// PR #7001). An operation delivered as a message still does: its pipeline's scope is the one enlisted
/// (<see cref="IncomingMessageContext"/>).
/// </para>
/// <para>
/// So the activator attaches one of these to the message's transaction once it has enlisted it, and
/// <see cref="IncomingMessageAwareBus"/> makes the copy in the transaction this begins instead, which no enlistment has seen. It
/// carries the message's step context, which is where Rebus finds the message to copy. The copy is therefore sent as one sent in
/// the message's transaction would be, but not through the outbox:
/// </para>
/// <list type="bullet">
/// <item><description>
/// when the message's transaction commits, from a commit callback registered after the activator's save of the unit of work and
/// the enlistments, so only once the handlers' writes and sends have been saved. A save that fails sends nothing, and the message is
/// redelivered;
/// </description></item>
/// <item><description>
/// never for a message whose transaction ends without committing, as it does when a handler throws: the transaction is then
/// disposed with the message's, unsent.
/// </description></item>
/// </list>
/// <para>
/// Once the message's transaction has started committing, a copy is no longer made here: what calls then, such as work a handler
/// started and did not wait for, is left to Rebus in whatever transaction is ambient, as it would be without this.
/// </para>
/// </remarks>
internal sealed class IncomingMessageCopyTransaction : IDisposable
{
    private const string Key = "blueprint-incoming-message-copy-transaction";

    private readonly object _gate = new();
    private readonly ITransactionContext _received;

    private RebusTransactionScope? _scope;
    private bool _ended;

    private IncomingMessageCopyTransaction(ITransactionContext received)
    {
        this._received = received;
    }

    /// <summary>
    /// Makes copies of the message received in <paramref name="received"/> be sent in a transaction of their own, which is
    /// completed when <paramref name="received"/> commits and disposed with it.
    /// </summary>
    /// <remarks>
    /// Call it before any handler runs and after the enlistments, so that the commit callback registered here runs after the save
    /// of the unit of work and after any commit callback an enlistment registered.
    /// </remarks>
    /// <param name="received">The transaction of the message being handled, which an outbox has been enlisted in.</param>
    public static void Attach(ITransactionContext received)
    {
        ArgumentNullException.ThrowIfNull(received);

        var copies = new IncomingMessageCopyTransaction(received);

        received.Items[Key] = copies;
        received.OnCommit(_ => copies.CommitAsync());
        received.OnDisposed(_ => copies.Dispose());
    }

    /// <summary>
    /// Gets the transaction to make a copy of the message being handled in, when <paramref name="ambient"/> is the transaction of a
    /// message that one was attached to and that has not started committing.
    /// </summary>
    /// <param name="ambient">The ambient transaction, which in a plain handler is that of the message being handled.</param>
    /// <returns>The transaction, begun on first use, or <see langword="null"/> when there is none to use.</returns>
    public static ITransactionContext? Find(ITransactionContext? ambient)
    {
        return ambient?.Items is { } items && items.TryGetValue(Key, out var value) && value is IncomingMessageCopyTransaction copies
            ? copies.GetOrBegin()
            : null;
    }

    /// <summary>
    /// Disposes the copies' transaction, unsent unless it was completed, once the message's transaction is disposed. Rebus calls
    /// this from <see cref="ITransactionContext.OnDisposed"/>, whether or not the message's transaction committed.
    /// </summary>
    public void Dispose()
    {
        RebusTransactionScope? scope;

        lock (this._gate)
        {
            this._ended = true;
            scope = this._scope;
            this._scope = null;
        }

        if (scope is null)
        {
            return;
        }

        // Disposing the scope makes ambient whatever was ambient when it began, which must not reach whoever is disposing the
        // message's transaction
        var ambient = AmbientTransactionContext.Current;

        try
        {
            scope.Dispose();
        }
        finally
        {
            AmbientTransactionContext.SetCurrent(ambient);
        }
    }

    private ITransactionContext? GetOrBegin()
    {
        lock (this._gate)
        {
            if (this._ended)
            {
                return null;
            }

            if (this._scope is null)
            {
                // Where Rebus finds the message to copy; without it there is nothing to copy, and Rebus says so
                if (!this._received.Items.TryGetValue(StepContext.StepContextKey, out var stepContext))
                {
                    return null;
                }

                // A scope is the public way to make a Rebus transaction. It makes itself ambient, which is put back at once: it is
                // made ambient only for the copy, by the caller
                var ambient = AmbientTransactionContext.Current;

                this._scope = new RebusTransactionScope();

                AmbientTransactionContext.SetCurrent(ambient);

                this._scope.TransactionContext.Items[StepContext.StepContextKey] = stepContext;
            }

            return this._scope.TransactionContext;
        }
    }

    private Task CommitAsync()
    {
        RebusTransactionScope? scope;

        lock (this._gate)
        {
            this._ended = true;
            scope = this._scope;
        }

        return scope is null ? Task.CompletedTask : scope.CompleteAsync();
    }
}

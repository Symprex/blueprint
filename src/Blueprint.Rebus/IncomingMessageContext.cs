using Rebus.Pipeline;

namespace Blueprint.Rebus;

/// <summary>
/// Holds the Rebus context of the message that a Blueprint pipeline is handling, for as long as it is being handled.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="BlueprintApiActivator"/> runs an operation delivered as a message inside a
/// <see cref="global::Rebus.Transport.RebusTransactionScope"/> of its own, and the generated pipeline opens another before the
/// handler, so that what the handler publishes and sends goes out with its unit of work. Each scope replaces the ambient
/// transaction context, which is where Rebus finds the message being handled, so the activator captures the context of that
/// message here before then, for <see cref="IncomingMessageAwareBus"/> and the <see cref="IMessageContext"/> that
/// <c>AddBlueprintRebus</c> registers.
/// </para>
/// <para>
/// A plain <c>IHandleMessages&lt;T&gt;</c> is not run through a pipeline, so nothing hides its message from Rebus and
/// nothing is captured for it. Rebus calls it after the activator has returned its handlers, so nothing set in a flow of the
/// activator's would reach it anyway. What it needs instead, when an outbox is enlisted in its message's own transaction, is
/// attached to that transaction (<see cref="IncomingMessageCopyTransaction"/>).
/// </para>
/// </remarks>
internal static class IncomingMessageContext
{
    private static readonly AsyncLocal<Holder?> _current = new();

    /// <summary>
    /// Gets the context of the message being handled, which is <c>null</c> when not handling a message through a pipeline,
    /// including in work that a handler started and that carries on after the message has been handled.
    /// </summary>
    public static IMessageContext? Current => _current.Value?.Context;

    /// <summary>
    /// Makes <paramref name="context"/> the context of the message being handled until the returned scope is disposed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call it from an async method, so that the value set here stays in that method's flow and in what it starts, and never
    /// reaches its caller: an async method's changes to the execution context are undone when it returns to its caller.
    /// </para>
    /// <para>
    /// An async local value is copied to everything started from the flow that set it, including work that outlives the
    /// handling of the message, such as a task the handler does not wait for. So the context is kept in a holder that every
    /// copy shares, which disposing the scope empties, so that the message is then current nowhere rather than still current
    /// wherever such work carries on, where its transaction may already have completed (the reason
    /// <c>HttpContextAccessor</c> holds its context in the same way).
    /// </para>
    /// </remarks>
    /// <param name="context">The context of the message being handled.</param>
    /// <returns>A scope that, once disposed, ends the context being current.</returns>
    public static IDisposable Begin(IMessageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var holder = new Holder(context);

        _current.Value = holder;

        return holder;
    }

    private sealed class Holder(IMessageContext context) : IDisposable
    {
        private volatile IMessageContext? _context = context;

        public IMessageContext? Context => this._context;

        public void Dispose()
        {
            this._context = null;
        }
    }
}

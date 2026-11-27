using JetBrains.Annotations;

namespace Blueprint.Persistence;

/// <summary>
/// Reruns the operation when its unit of work save is refused because another request wrote first
/// (<see cref="ConcurrencyConflictException"/>), so a handler that reads before it writes — a find-or-create — sees what the other
/// request wrote and can answer with it. After the last rerun is refused too, the answer is the operation's
/// <see cref="ConcurrencyConflictErrorAttribute"/> 409.
/// </summary>
/// <remarks>
/// <para>
/// Each rerun runs the whole generated pipeline again, in a fresh DI scope and so a fresh unit of work. What the refused
/// attempt left in scoped buffers is thrown away with it: its store writes, its outbox messages (Rebus sends in the
/// pipeline's transaction, an <c>IMessageOutbox</c>), its background tasks. Nothing is audited for it. Only a root
/// execution is rerun; a nested one lets the refusal through to its root. A cancelled operation is not rerun.
/// </para>
/// <para>
/// It is the author's statement that the handler is safe to run again. A handler that calls outside directly (HTTP, a
/// payment provider, email) makes that call again on every rerun, so every such call must carry an idempotency key that
/// is the same on every rerun and send the same request — or, better, be moved into a message sent through the outbox.
/// Writes a handler saved part-way, itself or through a nested command, are committed and stay. See
/// <c>docs/adr/0001-rerunning-an-operation-after-a-refused-save.md</c>.
/// </para>
/// <para>
/// The generator refuses it on an operation that does not save (<c>[DoNotSaveChanges]</c>, BPP003), on one with
/// <c>[NoRebusTransaction]</c>, whose sends would survive the refusal (BPP004), and with a count outside 1–5 (BPP005).
/// </para>
/// </remarks>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class RetryOnConflictAttribute : Attribute
{
    /// <summary>
    /// The most reruns an operation may ask for.
    /// </summary>
    public const int MaxRetries = 5;

    /// <summary>
    /// Initialises a new instance of the <see cref="RetryOnConflictAttribute"/> class.
    /// </summary>
    /// <param name="retries">How many times to rerun the operation after a refused save, 1 to <see cref="MaxRetries"/>.</param>
    public RetryOnConflictAttribute(int retries = 1)
    {
        this.Retries = retries;
    }

    /// <summary>
    /// Gets how many times the operation is rerun after a refused save.
    /// </summary>
    public int Retries { get; }
}

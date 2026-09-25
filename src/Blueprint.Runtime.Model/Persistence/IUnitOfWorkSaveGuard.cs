using JetBrains.Annotations;

namespace Blueprint.Persistence;

/// <summary>
/// Implemented by an <see cref="IUnitOfWork"/> that can tell whether work was queued on it after it was saved: work that
/// no save will then write.
/// </summary>
/// <remarks>
/// <para>
/// Blueprint.Rebus calls <see cref="ThrowIfChangedSinceSave"/> once a Rebus transaction that a consumer's outbox is
/// enlisted in has run its commit callbacks, on both the paths that enlist one: a generated pipeline's scope, and a
/// received message a plain handler ran in. An outbox that queues its rows on the unit of work from a commit callback — as
/// a direct port of legacy's <c>UseMartenSessionAsOutbox</c> would, because legacy committed its Rebus transaction before
/// its save — queues them after the save, where they are never written. This turns that silent loss into a failure.
/// </para>
/// <para>
/// A unit of work that does not implement this is not checked.
/// </para>
/// </remarks>
[PublicAPI]
public interface IUnitOfWorkSaveGuard
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when the unit of work has been saved and work has been queued on it
    /// since. Does nothing when it has not been saved, or has nothing queued.
    /// </summary>
    /// <exception cref="InvalidOperationException">Work was queued on the unit of work after it was saved.</exception>
    void ThrowIfChangedSinceSave();
}

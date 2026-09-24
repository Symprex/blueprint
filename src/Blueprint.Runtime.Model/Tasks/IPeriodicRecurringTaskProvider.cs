using JetBrains.Annotations;

namespace Blueprint.Tasks;

/// <summary>
/// An <see cref="IRecurringTaskProvider"/> that runs the registration of the app's recurring task schedules itself, on a
/// cadence of its own, as legacy Blueprint's Hangfire provider did with its <c>{DefaultQueue}:Scheduler</c> job.
/// </summary>
/// <remarks>
/// <para>
/// When the registered provider implements this, the host's start-up calls <see cref="SetupRecurringManagerAsync"/> and
/// nothing else, as legacy's start-up called only its provider's <c>SetupRecurringManagerAsync</c>. It neither asks a
/// scheduler for its schedules nor hands any to the provider: registering them, and removing the jobs of those no longer
/// declared, is left to the provider's own recurring run of <c>RecurringTaskManager.RescheduleAllAsync</c>. So a replica
/// starting during a rolling deployment cannot remove a job another replica has just registered, a scheduler that fails
/// while the host starts cannot remove its jobs, and a scheduler or a schedule that fails cannot stop the host starting.
/// </para>
/// <para>
/// A provider that does not implement this has its schedules registered when the host starts, and only then.
/// </para>
/// </remarks>
[UsedImplicitly(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
public interface IPeriodicRecurringTaskProvider : IRecurringTaskProvider
{
    /// <summary>
    /// Installs, or updates, whatever makes the provider run the registration of every recurring task schedule on its
    /// cadence. Called once when the host starts.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the installation.</param>
    /// <returns>A task that completes when the provider's recurring registration is installed.</returns>
    Task SetupRecurringManagerAsync(CancellationToken cancellationToken = default);
}

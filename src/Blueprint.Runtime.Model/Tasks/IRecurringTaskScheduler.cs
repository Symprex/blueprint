using JetBrains.Annotations;

namespace Blueprint.Tasks;

/// <summary>
/// A recurring task scheduler is responsible for defining one or more <see cref="RecurringTaskSchedule" />
/// entries that represent <see cref="IBackgroundTask" />s to be executed on a regular basis.
/// </summary>
/// <remarks>
/// <para>
/// Consumer applications implement this interface to declare their recurring tasks. The registered
/// <see cref="IRecurringTaskProvider"/> (e.g., Hangfire) then reads these schedules and ensures
/// the tasks are dispatched at the correct cadence.
/// </para>
/// <para>
/// Each registered scheduler is queried to discover the desired set of schedules, meaning the
/// return value should reflect whatever is current at the point of execution.
/// </para>
/// </remarks>
[UsedImplicitly(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
public interface IRecurringTaskScheduler
{
    /// <summary>
    /// Gets the task schedules that should be registered with the recurring task provider.
    /// </summary>
    /// <returns>The task schedules to register.</returns>
    Task<IEnumerable<RecurringTaskSchedule>> GetTaskSchedulesAsync();
}

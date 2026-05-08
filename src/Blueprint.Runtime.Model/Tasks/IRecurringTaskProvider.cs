using JetBrains.Annotations;

namespace Blueprint.Tasks;

/// <summary>
/// Provides the infrastructure to register and manage recurring background tasks.
/// </summary>
/// <remarks>
/// <para>
/// Consumer applications implement this interface to bridge Blueprint's recurring task model to
/// a concrete scheduler like Hangfire (<c>IRecurringJobManager</c>) or Azure Functions Timer triggers.
/// </para>
/// <para>
/// The provider is responsible for ensuring that schedules defined by <see cref="IRecurringTaskScheduler"/>
/// implementations are registered with the underlying infrastructure, and that previously registered
/// schedules that are no longer present are cleaned up.
/// </para>
/// </remarks>
[UsedImplicitly(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
public interface IRecurringTaskProvider
{
    /// <summary>
    /// Registers or updates recurring task schedules with the underlying scheduling infrastructure.
    /// Called during application startup or on a regular refresh cadence.
    /// </summary>
    /// <param name="schedules">The full set of desired recurring task schedules.</param>
    /// <param name="cancellationToken">A token to cancel the registration.</param>
    /// <returns>A task representing the asynchronous registration.</returns>
    Task RegisterSchedulesAsync(
        IReadOnlyList<RecurringTaskSchedule> schedules,
        CancellationToken cancellationToken = default);
}

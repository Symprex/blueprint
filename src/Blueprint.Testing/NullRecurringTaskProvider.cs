using Blueprint.Tasks;

namespace Blueprint.Testing;

/// <summary>
/// A no-op <see cref="IRecurringTaskProvider"/> for test environments where recurring task
/// registration should be silently ignored.
/// </summary>
public sealed class NullRecurringTaskProvider : IRecurringTaskProvider
{
    /// <summary>
    /// Gets the singleton instance.
    /// </summary>
    public static NullRecurringTaskProvider Instance { get; } = new();

    /// <inheritdoc />
    public Task RegisterSchedulesAsync(
        IReadOnlyList<RecurringTaskSchedule> schedules,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

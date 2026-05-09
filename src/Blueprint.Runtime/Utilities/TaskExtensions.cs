namespace Blueprint.Utilities;

/// <summary>
/// Extension methods for <see cref="Task{T}"/>.
/// </summary>
public static class TaskExtensions
{
    /// <summary>
    /// Converts a <see cref="Task{T}"/> to a <see cref="Task{Object}"/>, boxing
    /// the result value. Preserves cancellation and exception semantics.
    /// </summary>
    /// <typeparam name="T">The original result type.</typeparam>
    /// <param name="task">The task to convert.</param>
    /// <returns>A <see cref="Task{Object}"/> that completes with the same outcome.</returns>
    public static async Task<object> ConvertToObject<T>(this Task<T> task)
    {
        return (await task.ConfigureAwait(false))!;
    }
}

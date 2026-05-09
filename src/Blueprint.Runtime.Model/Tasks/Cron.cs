using JetBrains.Annotations;

namespace Blueprint.Tasks;

/// <summary>
/// Provides factory methods for common CRON expressions used with <see cref="RecurringTaskSchedule"/>.
/// These produce standard 5-part cron strings compatible with Hangfire, NCrontab, and similar libraries.
/// </summary>
[PublicAPI]
public static class Cron
{
    /// <summary>
    /// Returns a cron expression that fires every minute.
    /// </summary>
    /// <returns>The cron expression <c>* * * * *</c>.</returns>
    public static string Minutely()
    {
        return "* * * * *";
    }

    /// <summary>
    /// Returns a cron expression that fires every N minutes.
    /// </summary>
    /// <param name="interval">The minute interval (e.g., 5 means every 5 minutes).</param>
    /// <returns>A cron expression like <c>*/5 * * * *</c>.</returns>
    public static string MinuteInterval(int interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(interval, 59);

        return $"*/{interval} * * * *";
    }

    /// <summary>
    /// Returns a cron expression that fires every hour at minute 0.
    /// </summary>
    /// <returns>The cron expression <c>0 * * * *</c>.</returns>
    public static string Hourly()
    {
        return "0 * * * *";
    }

    /// <summary>
    /// Returns a cron expression that fires every hour at the specified minute.
    /// </summary>
    /// <param name="minute">The minute past the hour (0-59).</param>
    /// <returns>A cron expression like <c>15 * * * *</c>.</returns>
    public static string Hourly(int minute)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minute, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minute, 59);

        return $"{minute} * * * *";
    }

    /// <summary>
    /// Returns a cron expression that fires daily at midnight (00:00).
    /// </summary>
    /// <returns>The cron expression <c>0 0 * * *</c>.</returns>
    public static string Daily()
    {
        return "0 0 * * *";
    }

    /// <summary>
    /// Returns a cron expression that fires daily at the specified hour and minute.
    /// </summary>
    /// <param name="hour">The hour of day (0-23).</param>
    /// <param name="minute">The minute past the hour (0-59).</param>
    /// <returns>A cron expression like <c>30 2 * * *</c>.</returns>
    public static string Daily(int hour, int minute = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(hour, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hour, 23);
        ArgumentOutOfRangeException.ThrowIfLessThan(minute, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minute, 59);

        return $"{minute} {hour} * * *";
    }

    /// <summary>
    /// Returns a cron expression that fires weekly on the specified day at midnight.
    /// </summary>
    /// <param name="dayOfWeek">The day of week (Sunday = 0).</param>
    /// <returns>A cron expression like <c>0 0 * * 1</c> (Monday).</returns>
    public static string Weekly(DayOfWeek dayOfWeek = DayOfWeek.Monday)
    {
        return $"0 0 * * {(int)dayOfWeek}";
    }

    /// <summary>
    /// Returns a cron expression that fires weekly on the specified day at the given time.
    /// </summary>
    /// <param name="dayOfWeek">The day of week (Sunday = 0).</param>
    /// <param name="hour">The hour of day (0-23).</param>
    /// <param name="minute">The minute past the hour (0-59).</param>
    /// <returns>A cron expression like <c>30 9 * * 1</c>.</returns>
    public static string Weekly(DayOfWeek dayOfWeek, int hour, int minute = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(hour, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hour, 23);
        ArgumentOutOfRangeException.ThrowIfLessThan(minute, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minute, 59);

        return $"{minute} {hour} * * {(int)dayOfWeek}";
    }

    /// <summary>
    /// Returns a cron expression that fires monthly on the 1st at midnight.
    /// </summary>
    /// <returns>The cron expression <c>0 0 1 * *</c>.</returns>
    public static string Monthly()
    {
        return "0 0 1 * *";
    }

    /// <summary>
    /// Returns a cron expression that fires monthly on the specified day at the given time.
    /// </summary>
    /// <param name="day">The day of month (1-28, using 28 avoids month-length issues).</param>
    /// <param name="hour">The hour of day (0-23).</param>
    /// <param name="minute">The minute past the hour (0-59).</param>
    /// <returns>A cron expression like <c>0 3 15 * *</c>.</returns>
    public static string Monthly(int day, int hour = 0, int minute = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(day, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(day, 28);
        ArgumentOutOfRangeException.ThrowIfLessThan(hour, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hour, 23);
        ArgumentOutOfRangeException.ThrowIfLessThan(minute, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minute, 59);

        return $"{minute} {hour} {day} * *";
    }
}

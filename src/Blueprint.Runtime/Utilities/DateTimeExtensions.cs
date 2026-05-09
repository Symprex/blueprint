namespace Blueprint.Utilities;

/// <summary>
/// Extension methods for <see cref="DateTime"/> and <see cref="DateTimeOffset"/>.
/// </summary>
public static class DateTimeExtensions
{
    private static readonly DateTime _unixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Gets the last second of the day (23:59:59.000) for the given date.
    /// </summary>
    /// <param name="dateTime">The date from which to get the end of the day.</param>
    /// <returns>The end of the day for the given <see cref="DateTime"/>.</returns>
    public static DateTime EndOfDay(this DateTime dateTime)
    {
        return dateTime.Date.AddDays(1).AddSeconds(-1);
    }

    /// <summary>
    /// Returns whether this date is in the future relative to <see cref="SystemTime.UtcNow"/>.
    /// </summary>
    /// <param name="date">The date to check.</param>
    /// <param name="temporalCheckType">The granularity of the comparison.</param>
    /// <returns><c>true</c> if the date is in the future.</returns>
    public static bool IsInFuture(this DateTime date, TemporalCheck temporalCheckType)
    {
        return temporalCheckType == TemporalCheck.DateTime
            ? date > SystemTime.UtcNow
            : date.Date > SystemTime.UtcNow.Date;
    }

    /// <summary>
    /// Returns whether this date is in the future relative to <see cref="SystemTime.UtcNow"/>.
    /// </summary>
    /// <param name="date">The date to check.</param>
    /// <param name="temporalCheckType">The granularity of the comparison.</param>
    /// <returns><c>true</c> if the date is in the future.</returns>
    public static bool IsInFuture(this DateTimeOffset date, TemporalCheck temporalCheckType)
    {
        return temporalCheckType == TemporalCheck.DateTime
            ? date > SystemTime.UtcNow
            : date.Date > SystemTime.UtcNow.Date;
    }

    /// <summary>
    /// Returns whether this date is in the past relative to <see cref="SystemTime.UtcNow"/>.
    /// </summary>
    /// <param name="date">The date to check.</param>
    /// <param name="temporalCheckType">The granularity of the comparison.</param>
    /// <returns><c>true</c> if the date is in the past.</returns>
    public static bool IsInPast(this DateTime date, TemporalCheck temporalCheckType)
    {
        return temporalCheckType == TemporalCheck.DateTime
            ? date < SystemTime.UtcNow
            : date.Date < SystemTime.UtcNow.Date;
    }

    /// <summary>
    /// Returns whether this date is in the past relative to <see cref="SystemTime.UtcNow"/>.
    /// </summary>
    /// <param name="date">The date to check.</param>
    /// <param name="temporalCheckType">The granularity of the comparison.</param>
    /// <returns><c>true</c> if the date is in the past.</returns>
    public static bool IsInPast(this DateTimeOffset date, TemporalCheck temporalCheckType)
    {
        return temporalCheckType == TemporalCheck.DateTime
            ? date < SystemTime.UtcNow
            : date.Date < SystemTime.UtcNow.Date;
    }

    /// <summary>
    /// Gets the previous date that falls on one of the specified days of the week.
    /// </summary>
    /// <param name="date">The starting date.</param>
    /// <param name="validDays">The days of the week to consider.</param>
    /// <returns>The most recent <see cref="DateTime"/> before <paramref name="date"/> that is one of the specified days.</returns>
    public static DateTime GetPreviousDate(this DateTime date, IEnumerable<DayOfWeek> validDays)
    {
        var dayOfWeeks = validDays as IList<DayOfWeek> ?? validDays.ToList();

        if (dayOfWeeks.Count == 0)
        {
            throw new ArgumentException("At least one valid day must be specified.", nameof(validDays));
        }

        date = date.AddDays(-1);

        while (!dayOfWeeks.Contains(date.DayOfWeek))
        {
            date = date.AddDays(-1);
        }

        return date;
    }

    /// <summary>
    /// Converts a UNIX timestamp in seconds to a <see cref="DateTime"/>.
    /// </summary>
    /// <param name="unixTime">The UNIX timestamp in seconds.</param>
    /// <returns>The equivalent <see cref="DateTime"/> in UTC.</returns>
    public static DateTime FromUnixTimeSeconds(this long unixTime)
    {
        return _unixEpoch.AddSeconds(unixTime);
    }

    /// <summary>
    /// Converts a UNIX timestamp in milliseconds to a <see cref="DateTime"/>.
    /// </summary>
    /// <param name="unixTime">The UNIX timestamp in milliseconds.</param>
    /// <returns>The equivalent <see cref="DateTime"/> in UTC.</returns>
    public static DateTime FromUnixTimeMilliseconds(this long unixTime)
    {
        return _unixEpoch.AddMilliseconds(unixTime);
    }

    /// <summary>
    /// Converts a <see cref="DateTime"/> to a UNIX timestamp in seconds.
    /// </summary>
    /// <param name="date">The date to convert.</param>
    /// <returns>The equivalent UNIX timestamp in seconds.</returns>
    public static long ToUnixTimeSeconds(this DateTime date)
    {
        return Convert.ToInt64((date - _unixEpoch).TotalSeconds);
    }

    /// <summary>
    /// Converts a <see cref="DateTime"/> to a UNIX timestamp in milliseconds.
    /// </summary>
    /// <param name="date">The date to convert.</param>
    /// <returns>The equivalent UNIX timestamp in milliseconds.</returns>
    public static long ToUnixTimeMilliseconds(this DateTime date)
    {
        return Convert.ToInt64((date - _unixEpoch).TotalMilliseconds);
    }
}

namespace Blueprint;

/// <summary>
/// Provides an abstraction over <see cref="DateTime"/>, and in particular the current time,
/// to allow easier testing of time-sensitive functions. Any function that requires access to
/// the current time should use <see cref="UtcNow"/> instead of <see cref="DateTime.UtcNow"/>.
/// </summary>
public static class SystemTime
{
    private static readonly AsyncLocal<SystemTimeModifier?> _modifier = new();

    /// <summary>
    /// Gets the current UTC date and time. In production, delegates to <see cref="DateTime.UtcNow"/>.
    /// Can be controlled in tests using <see cref="PauseForThread"/>.
    /// </summary>
    public static DateTime UtcNow => _modifier.Value?.UtcNow ?? DateTime.UtcNow;

    /// <summary>
    /// Gets the current local date and time. In production, delegates to <see cref="DateTime.Now"/>.
    /// Can be controlled in tests using <see cref="PauseForThread"/>.
    /// </summary>
    public static DateTime Now => _modifier.Value?.Now ?? DateTime.Now;

    /// <summary>
    /// Pauses time for the current async context to enable deterministic time control in tests.
    /// The returned modifier captures the current time at the moment of the call.
    /// </summary>
    /// <returns>A <see cref="SystemTimeModifier"/> that allows manipulation of time.</returns>
    public static SystemTimeModifier PauseForThread()
    {
        _modifier.Value = new SystemTimeModifier();
        return _modifier.Value;
    }

    /// <summary>
    /// Restores <see cref="UtcNow"/> and <see cref="Now"/> to their default behaviour.
    /// </summary>
    public static void ResumeForThread()
    {
        _modifier.Value = null;
    }

    /// <summary>
    /// Controls the value of <see cref="SystemTime.UtcNow"/> and <see cref="SystemTime.Now"/>
    /// within the current async context. Disposing the modifier restores real time.
    /// </summary>
    public sealed class SystemTimeModifier : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SystemTimeModifier"/> class,
        /// capturing the current real time as the starting point.
        /// </summary>
        internal SystemTimeModifier()
        {
            this.UtcNow = DateTime.UtcNow;
            this.Now = DateTime.Now;
        }

        /// <summary>Gets the controlled UTC time.</summary>
        public DateTime UtcNow { get; private set; }

        /// <summary>Gets the controlled local time.</summary>
        public DateTime Now { get; private set; }

        /// <summary>
        /// Sets both <see cref="UtcNow"/> and <see cref="Now"/> to the given value.
        /// </summary>
        /// <param name="dateTime">The date and time to set.</param>
        public void Set(DateTime dateTime)
        {
            this.UtcNow = dateTime;
            this.Now = dateTime;
        }

        /// <summary>
        /// Sets <see cref="UtcNow"/> only. The given value must have <see cref="DateTimeKind.Utc"/>.
        /// </summary>
        /// <param name="dateTime">The UTC date and time to set.</param>
        public void SetUtcNow(DateTime dateTime)
        {
            if (dateTime.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("DateTime must have kind of UTC.", nameof(dateTime));
            }

            this.UtcNow = dateTime;
        }

        /// <summary>
        /// Advances both <see cref="UtcNow"/> and <see cref="Now"/> by the given amount.
        /// </summary>
        /// <param name="timeSpan">The amount to advance.</param>
        public void FastForward(TimeSpan timeSpan)
        {
            this.UtcNow = this.UtcNow.Add(timeSpan);
            this.Now = this.Now.Add(timeSpan);
        }

        /// <summary>
        /// Rewinds both <see cref="UtcNow"/> and <see cref="Now"/> by the given amount.
        /// </summary>
        /// <param name="timeSpan">The amount to rewind.</param>
        public void Rewind(TimeSpan timeSpan)
        {
            this.UtcNow = this.UtcNow.Subtract(timeSpan);
            this.Now = this.Now.Subtract(timeSpan);
        }

        /// <summary>
        /// Advances <see cref="UtcNow"/> only by the given amount.
        /// </summary>
        /// <param name="timeSpan">The amount to advance.</param>
        public void FastForwardUtc(TimeSpan timeSpan)
        {
            this.UtcNow = this.UtcNow.Add(timeSpan);
        }

        /// <summary>
        /// Rewinds <see cref="UtcNow"/> only by the given amount.
        /// </summary>
        /// <param name="timeSpan">The amount to rewind.</param>
        public void RewindUtc(TimeSpan timeSpan)
        {
            this.UtcNow = this.UtcNow.Subtract(timeSpan);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            ResumeForThread();
        }
    }
}


namespace Blueprint.Utilities;

/// <summary>
/// Specifies the granularity of a temporal comparison.
/// </summary>
public enum TemporalCheck
{
    /// <summary>
    /// Compare using the full date and time precision.
    /// </summary>
    DateTime,

    /// <summary>
    /// Compare using only the date component, ignoring time.
    /// </summary>
    DateOnly,
}

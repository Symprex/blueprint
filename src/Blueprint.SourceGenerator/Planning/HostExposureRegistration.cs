using Blueprint.SourceGenerator.Helpers;

namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// Describes one host exposure discovered during descriptor planning so later contributors can project the consolidated
/// host list without reparsing operation attributes.
/// </summary>
/// <param name="HostName">The stable host name.</param>
public sealed record HostExposureRegistration(string HostName)
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HostExposureRegistration"/> class with optional source-location metadata.
    /// </summary>
    /// <param name="hostName">The stable host name.</param>
    /// <param name="locationInfo">The source location that declared the host exposure, when available.</param>
    public HostExposureRegistration(string hostName, LocationInfo? locationInfo)
        : this(hostName)
    {
        this.LocationInfo = locationInfo;
    }

    /// <summary>
    /// Gets the source location that declared the host exposure, when available.
    /// </summary>
    public LocationInfo? LocationInfo { get; init; }

    /// <summary>
    /// Gets the shared registration for Blueprint's current HTTP host surface.
    /// </summary>
    public static HostExposureRegistration Http { get; } = new("http");

    /// <summary>
    /// Gets the shared registration for Blueprint's current tasks host surface.
    /// </summary>
    public static HostExposureRegistration Tasks { get; } = new("tasks");

    /// <summary>
    /// Creates the shared HTTP host registration while preserving the source location that declared it.
    /// </summary>
    /// <param name="locationInfo">The host-exposure source location.</param>
    /// <returns>The HTTP host registration.</returns>
    public static HostExposureRegistration HttpAt(LocationInfo? locationInfo)
    {
        return locationInfo is not null ? new("http", locationInfo) : Http;
    }

    /// <summary>
    /// Creates the shared tasks host registration while preserving the source location that declared it.
    /// </summary>
    /// <param name="locationInfo">The host-exposure source location.</param>
    /// <returns>The tasks host registration.</returns>
    public static HostExposureRegistration TasksAt(LocationInfo locationInfo)
    {
        return new("tasks", locationInfo);
    }

    /// <inheritdoc />
    public bool Equals(HostExposureRegistration? other)
    {
        return other is not null
            && StringComparer.Ordinal.Equals(this.HostName, other.HostName);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(this.HostName);
    }
}

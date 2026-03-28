namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// Describes one host exposure discovered during descriptor planning so later contributors can project the consolidated
/// host list without reparsing operation attributes.
/// </summary>
/// <param name="HostName">The stable host name.</param>
public sealed record HostExposureRegistration(string HostName)
{
    /// <summary>
    /// Gets the shared registration for Blueprint's current HTTP host surface.
    /// </summary>
    public static HostExposureRegistration Http { get; } = new("http");
}

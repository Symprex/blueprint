namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Describes a host contributor that enables one or more stable host names for descriptor validation.
/// </summary>
public interface IEnabledHostContributor : IHostContributor
{
    /// <summary>
    /// Gets the stable host names enabled by this contributor.
    /// </summary>
    IReadOnlyList<string> HostNames { get; }
}

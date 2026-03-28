using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Validates that descriptor-planned host exposure maps to enabled host contributors before descriptor or manifest output is used.
/// </summary>
internal static class HostExposureDiagnostics
{
    /// <summary>
    /// Adds diagnostics for host-exposure metadata that does not map to any enabled host contributor in the active execution plan.
    /// </summary>
    /// <param name="descriptorPlan">The descriptor plan under validation.</param>
    /// <param name="hostContributors">The host contributors enabled for the current configuration.</param>
    public static void AddUnsupportedHostDiagnostics(
        DescriptorPlan descriptorPlan,
        IEnumerable<IHostContributor> hostContributors)
    {
        var enabledHostNames = hostContributors
            .OfType<IEnabledHostContributor>()
            .SelectMany(static contributor => contributor.HostNames)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var hostExposure in descriptorPlan.GetMetadata<HostExposureRegistration>()
                     .GroupBy(static registration => registration.HostName, StringComparer.Ordinal)
                     .Select(static registrations => registrations.First())
                     .OrderBy(static registration => registration.HostName, StringComparer.Ordinal))
        {
            if (enabledHostNames.Contains(hostExposure.HostName))
            {
                continue;
            }

            descriptorPlan.AddDiagnostic(
                new DiagnosticInfo(
                    OperationDiscoveryDiagnosticDescriptors.UnsupportedHostExposure,
                    hostExposure.LocationInfo,
                    [hostExposure.HostName, descriptorPlan.Operation.TypeName]));
        }
    }
}

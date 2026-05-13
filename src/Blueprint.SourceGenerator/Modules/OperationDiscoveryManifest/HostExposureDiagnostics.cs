using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Validates that descriptor-planned host exposure maps to enabled host contributors and does not target multiple
/// enabled hosts before descriptor or manifest output is used.
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
        var enabledHostNames = GetEnabledHostNames(hostContributors);

        foreach (var hostExposure in GetDistinctHostExposureRegistrations(descriptorPlan))
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

    /// <summary>
    /// Adds a diagnostic when one operation is exposed to more than one enabled host contributor.
    /// </summary>
    /// <param name="descriptorPlan">The descriptor plan under validation.</param>
    /// <param name="hostContributors">The host contributors enabled for the current configuration.</param>
    public static void AddConflictingHostDiagnostics(
        DescriptorPlan descriptorPlan,
        IEnumerable<IHostContributor> hostContributors)
    {
        var enabledHostNames = GetEnabledHostNames(hostContributors);
        var enabledHostExposure = GetDistinctHostExposureRegistrations(descriptorPlan)
            .Where(registration => enabledHostNames.Contains(registration.HostName))
            .ToArray();

        if (enabledHostExposure.Length <= 1)
        {
            return;
        }

        var locationInfo = enabledHostExposure[1].LocationInfo ?? enabledHostExposure[0].LocationInfo;
        descriptorPlan.AddDiagnostic(
            new DiagnosticInfo(
                OperationDiscoveryDiagnosticDescriptors.ConflictingHostExposure,
                locationInfo,
                [
                    descriptorPlan.Operation.TypeName,
                    string.Join(", ", enabledHostExposure.Select(static registration => registration.HostName)),
                ]));
    }

    private static HashSet<string> GetEnabledHostNames(IEnumerable<IHostContributor> hostContributors)
    {
        return hostContributors
            .OfType<IEnabledHostContributor>()
            .SelectMany(static contributor => contributor.HostNames)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HostExposureRegistration[] GetDistinctHostExposureRegistrations(DescriptorPlan descriptorPlan)
    {
        return descriptorPlan.GetMetadata<HostExposureRegistration>()
            .DistinctBy(static registration => registration.HostName, StringComparer.Ordinal)
            .OrderBy(static registration => registration.HostName, StringComparer.Ordinal)
            .ToArray();
    }
}

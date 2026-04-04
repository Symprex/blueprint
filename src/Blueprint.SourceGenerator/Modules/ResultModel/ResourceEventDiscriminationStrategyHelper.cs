using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Shared helper for recognizing same-status result groups that are safely discriminated by compile-time-proven
/// resource-event metadata rather than by distinct status/body shapes alone.
/// </summary>
internal static class ResourceEventDiscriminationStrategyHelper
{
    /// <summary>
    /// Determines whether every supplied result type has compile-time-proven resource-event metadata with a distinct
    /// serialized event identifier.
    /// </summary>
    /// <param name="context">The current descriptor contributor context.</param>
    /// <param name="resultTypes">The result types participating in the same-status group.</param>
    /// <returns><c>true</c> when every result type has unique compile-time event-id metadata; otherwise <c>false</c>.</returns>
    public static bool HasDistinctCompileTimeEventIds(
        DescriptorContributorContext context,
        IEnumerable<ITypeSymbol> resultTypes)
    {
        var registrationsByResultType = context.GetMetadata<ResourceEventMetadataRegistration>()
            .GroupBy(static registration => registration.ResultTypeName, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        if (registrationsByResultType.Count == 0)
        {
            return false;
        }

        var matchedCount = 0;
        var distinctEventIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resultType in resultTypes)
        {
            if (!registrationsByResultType.TryGetValue(GetTypeName(resultType), out var registration))
            {
                return false;
            }

            if (!distinctEventIds.Add(registration.EventId))
            {
                return false;
            }

            matchedCount++;
        }

        return matchedCount > 1;
    }

    private static string GetTypeName(ITypeSymbol typeSymbol)
    {
        return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal);
    }
}

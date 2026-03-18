namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Common metadata used to order and validate contributor composition.
/// </summary>
public interface IBlueprintContributor
{
    /// <summary>
    /// Gets the stable contributor identifier used for ordering and dependency declarations.
    /// </summary>
    string ContributorId { get; }

    /// <summary>
    /// Gets the explicit ordering hint for this contributor. Lower values run first.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Gets a value indicating whether only one contributor with this identifier may exist in a configuration.
    /// </summary>
    bool IsUnique { get; }

    /// <summary>
    /// Gets the contributor identifiers that must run before this contributor.
    /// </summary>
    IReadOnlyList<string> DependsOn { get; }
}

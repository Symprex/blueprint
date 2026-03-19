namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// Tracks produced and consumed variables across pipeline planning.
/// </summary>
public sealed class VariableRegistry
{
    private readonly Dictionary<VariableKey, VariableProvision> _uniqueProvisions = new();
    private readonly List<VariableProvision> _provisions = [];
    private readonly List<VariableRequirement> _requirements = [];

    /// <summary>
    /// Gets the registered provisions.
    /// </summary>
    public IReadOnlyList<VariableProvision> Provisions => this._provisions;

    /// <summary>
    /// Gets the registered requirements.
    /// </summary>
    public IReadOnlyList<VariableRequirement> Requirements => this._requirements;

    /// <summary>
    /// Registers a variable provision.
    /// </summary>
    /// <param name="provision">The provision to register.</param>
    public void RegisterProvision(VariableProvision provision)
    {
        if (!provision.AllowMultiple
            && this._uniqueProvisions.TryGetValue(provision.Key, out var existingProvision))
        {
            throw new InvalidOperationException(
                $"A unique variable provision for '{provision.Key.Purpose}' and type '{provision.Key.TypeName}' is already registered by '{existingProvision.ProducerId}'.");
        }

        if (!provision.AllowMultiple)
        {
            this._uniqueProvisions.Add(provision.Key, provision);
        }

        this._provisions.Add(provision);
    }

    /// <summary>
    /// Registers a variable requirement.
    /// </summary>
    /// <param name="requirement">The requirement to register.</param>
    public void RegisterRequirement(VariableRequirement requirement)
    {
        this._requirements.Add(requirement);
    }

    /// <summary>
    /// Gets the required variables that have no matching provision or are only available after the consumer stage.
    /// </summary>
    /// <param name="contributions">The planned pipeline contributions for the current operation.</param>
    /// <returns>The unsatisfied requirements.</returns>
    public IReadOnlyList<UnsatisfiedVariableRequirement> GetUnsatisfiedRequirements(IReadOnlyList<PipelineContribution> contributions)
    {
        var earliestContributionStages = contributions
            .GroupBy(contribution => contribution.ContributorId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Min(contribution => contribution.Stage),
                StringComparer.Ordinal);

        return this._requirements
            .Where(requirement => requirement.IsRequired)
            .Select(requirement => this.GetUnsatisfiedRequirement(requirement, earliestContributionStages))
            .Where(result => result is not null)
            .Select(result => result!)
            .ToArray();
    }

    private UnsatisfiedVariableRequirement? GetUnsatisfiedRequirement(
        VariableRequirement requirement,
        IReadOnlyDictionary<string, PipelineStage> earliestContributionStages)
    {
        var matchingProvisions = this._provisions
            .Where(provision => provision.Key == requirement.Key)
            .OrderBy(provision => provision.AvailableFrom)
            .ToArray();

        earliestContributionStages.TryGetValue(requirement.ConsumerId, out var requiredBy);
        var hasRequiredStage = earliestContributionStages.ContainsKey(requirement.ConsumerId);

        if (matchingProvisions.Length == 0)
        {
            return new UnsatisfiedVariableRequirement(
                requirement,
                hasRequiredStage ? requiredBy : null,
                EarliestAvailableFrom: null);
        }

        var earliestProvision = matchingProvisions[0];

        if (hasRequiredStage && earliestProvision.AvailableFrom > requiredBy)
        {
            return new UnsatisfiedVariableRequirement(
                requirement,
                requiredBy,
                earliestProvision.AvailableFrom);
        }

        return null;
    }
}

/// <summary>
/// Identifies a variable by type and purpose.
/// </summary>
/// <param name="TypeName">The variable type name.</param>
/// <param name="Purpose">The variable purpose.</param>
public sealed record VariableKey(string TypeName, string Purpose);

/// <summary>
/// Describes a value produced by a contributor.
/// </summary>
/// <param name="Key">The variable key.</param>
/// <param name="ProducerId">The producing contributor identifier.</param>
/// <param name="AvailableFrom">The stage at which the value becomes available.</param>
/// <param name="SourceKind">The kind of source backing the value.</param>
/// <param name="Lifetime">The preferred lifetime or storage model.</param>
/// <param name="AllowMultiple">Whether multiple producers are allowed.</param>
public sealed record VariableProvision(
    VariableKey Key,
    string ProducerId,
    PipelineStage AvailableFrom,
    VariableSourceKind SourceKind,
    VariableLifetime Lifetime,
    bool AllowMultiple = false);

/// <summary>
/// Describes a value consumed by a contributor.
/// </summary>
/// <param name="Key">The variable key.</param>
/// <param name="ConsumerId">The consuming contributor identifier.</param>
/// <param name="IsRequired">Whether the value is required.</param>
public sealed record VariableRequirement(
    VariableKey Key,
    string ConsumerId,
    bool IsRequired);

/// <summary>
/// Describes a required variable that is missing or only available too late for the consumer.
/// </summary>
/// <param name="Requirement">The unsatisfied requirement.</param>
/// <param name="RequiredBy">The earliest stage at which the consumer contributes to the pipeline.</param>
/// <param name="EarliestAvailableFrom">The earliest stage at which a matching provision becomes available.</param>
public sealed record UnsatisfiedVariableRequirement(
    VariableRequirement Requirement,
    PipelineStage? RequiredBy,
    PipelineStage? EarliestAvailableFrom);

/// <summary>
/// Identifies where a variable comes from.
/// </summary>
public enum VariableSourceKind
{
    Context,
    Service,
    GeneratedLocal,
    ProducedValue,
}

/// <summary>
/// Identifies where a variable should live in generated code.
/// </summary>
public enum VariableLifetime
{
    ContextLookup,
    ConstructorField,
    Local,
}

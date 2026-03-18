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

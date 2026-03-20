namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// Tracks produced and consumed variables across pipeline planning.
/// </summary>
public sealed class VariableRegistry
{
    private readonly Dictionary<VariableKey, VariableRegistration> _uniqueRegistrations = new();
    private readonly List<VariableRegistration> _registrations = [];
    private readonly List<VariableRequirement> _requirements = [];

    /// <summary>
    /// Gets the registered variable registrations.
    /// </summary>
    public IReadOnlyList<VariableRegistration> Registrations => this._registrations;

    /// <summary>
    /// Gets the registered requirements.
    /// </summary>
    public IReadOnlyList<VariableRequirement> Requirements => this._requirements;

    /// <summary>
    /// Registers a variable.
    /// </summary>
    /// <param name="registration">The variable registration.</param>
    public void RegisterVariable(VariableRegistration registration)
    {
        if (!registration.AllowMultiple
            && this._uniqueRegistrations.TryGetValue(registration.Key, out var existingRegistration))
        {
            throw new InvalidOperationException(
                $"A unique variable registration for '{registration.Key.Purpose}' and type '{registration.Key.TypeName}' is already registered by '{existingRegistration.ProducerId}'.");
        }

        if (!registration.AllowMultiple)
        {
            this._uniqueRegistrations.Add(registration.Key, registration);
        }

        this._registrations.Add(registration);
    }

    /// <summary>
    /// Registers a required variable.
    /// </summary>
    /// <param name="requirement">The requirement to register.</param>
    public void RequireVariable(VariableRequirement requirement)
    {
        this._requirements.Add(requirement);
    }

    /// <summary>
    /// Gets the required variables that have no matching registration or are only available after the consumer stage.
    /// </summary>
    /// <returns>The unsatisfied requirements.</returns>
    public IReadOnlyList<UnsatisfiedVariableRequirement> GetUnsatisfiedRequirements()
    {
        return this._requirements
            .Where(requirement => requirement.IsRequired)
            .Select(this.GetUnsatisfiedRequirement)
            .Where(result => result is not null)
            .Select(result => result!)
            .ToArray();
    }

    private UnsatisfiedVariableRequirement? GetUnsatisfiedRequirement(VariableRequirement requirement)
    {
        var matchingRegistrations = this._registrations
            .Where(registration => registration.Key == requirement.Key)
            .OrderBy(registration => registration.AvailableFrom)
            .ToArray();

        if (matchingRegistrations.Length == 0)
        {
            return new UnsatisfiedVariableRequirement(requirement, EarliestAvailableFrom: null);
        }

        var earliestRegistration = matchingRegistrations[0];

        if (earliestRegistration.AvailableFrom > requirement.RequiredBy)
        {
            return new UnsatisfiedVariableRequirement(
                requirement,
                earliestRegistration.AvailableFrom);
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
/// Describes a variable registered by a contributor.
/// </summary>
/// <param name="Key">The variable key.</param>
/// <param name="ProducerId">The producing contributor identifier.</param>
/// <param name="AvailableFrom">The stage at which the value becomes available.</param>
/// <param name="SourceKind">The kind of source backing the value.</param>
/// <param name="Lifetime">The preferred lifetime or storage model.</param>
/// <param name="AllowMultiple">Whether multiple producers are allowed.</param>
public sealed record VariableRegistration(
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
/// <param name="RequiredBy">The stage by which the variable must be available.</param>
/// <param name="IsRequired">Whether the value is required.</param>
public sealed record VariableRequirement(
    VariableKey Key,
    string ConsumerId,
    PipelineStage RequiredBy,
    bool IsRequired);

/// <summary>
/// Describes a required variable that is missing or only available too late for the consumer.
/// </summary>
/// <param name="Requirement">The unsatisfied requirement.</param>
/// <param name="EarliestAvailableFrom">The earliest stage at which a matching registration becomes available.</param>
public sealed record UnsatisfiedVariableRequirement(
    VariableRequirement Requirement,
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

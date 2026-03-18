namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// Mutable app-registration planning state for a single operation.
/// </summary>
/// <param name="Operation">The operation being planned.</param>
public sealed record AppRegistrationPlan(OperationModel Operation)
{
    private readonly List<string> _registrations = [];

    /// <summary>
    /// Gets the registration expressions.
    /// </summary>
    public IReadOnlyList<string> Registrations => this._registrations;

    /// <summary>
    /// Adds a registration expression.
    /// </summary>
    /// <param name="registration">The registration expression.</param>
    public void AddRegistration(string registration)
    {
        this._registrations.Add(registration);
    }
}

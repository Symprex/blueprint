using System.Diagnostics;

namespace Blueprint.HttpClient;

/// <summary>
/// Includes an operation among those a <see cref="BlueprintClientAttribute"/> client calls.
/// </summary>
[Conditional("CODE_ANALYSIS")]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class BlueprintClientIncludeAttribute : Attribute
{
    /// <summary>
    /// Initialises a new instance of the <see cref="BlueprintClientIncludeAttribute"/> class.
    /// </summary>
    /// <param name="operationType">The operation's type.</param>
    public BlueprintClientIncludeAttribute(Type operationType)
    {
        this.OperationType = operationType ?? throw new ArgumentNullException(nameof(operationType));
    }

    /// <summary>
    /// Gets the operation's type.
    /// </summary>
    public Type OperationType { get; }
}

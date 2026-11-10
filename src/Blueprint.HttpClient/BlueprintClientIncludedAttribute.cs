using System.ComponentModel;

namespace Blueprint.HttpClient;

/// <summary>
/// Records, on the generated half of a <see cref="BlueprintClientAttribute"/> client, an operation the client calls.
/// </summary>
/// <remarks>
/// Unlike <see cref="BlueprintClientIncludeAttribute"/>, which is conditional and so not compiled into the client's
/// assembly, this one is, so that another assembly's generator can tell which operations a referenced client includes: a
/// gateway operation marked <c>[Expose&lt;TInner&gt;]</c> forwards through the one client that includes <c>TInner</c>.
/// The source generator emits it; nothing applies it by hand.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class BlueprintClientIncludedAttribute : Attribute
{
    /// <summary>
    /// Initialises a new instance of the <see cref="BlueprintClientIncludedAttribute"/> class.
    /// </summary>
    /// <param name="operationType">The operation's type.</param>
    public BlueprintClientIncludedAttribute(Type operationType)
    {
        this.OperationType = operationType ?? throw new ArgumentNullException(nameof(operationType));
    }

    /// <summary>
    /// Gets the operation's type.
    /// </summary>
    public Type OperationType { get; }
}

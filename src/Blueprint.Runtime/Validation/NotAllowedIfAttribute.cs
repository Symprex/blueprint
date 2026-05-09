using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Blueprint.Validation;

/// <summary>
/// Forbids setting a value when another property matches one of the configured dependent values.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class NotAllowedIfAttribute : ValidationAttribute
{
    private readonly HashSet<string?> _dependentValueTexts;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotAllowedIfAttribute"/> class.
    /// </summary>
    /// <param name="dependentProperty">The sibling property whose value activates the prohibition.</param>
    /// <param name="dependentValue">The dependent value, or values, that activate the prohibition.</param>
    public NotAllowedIfAttribute(string dependentProperty, object? dependentValue)
        : base($"This field is not allowed to be set if {dependentProperty} field is {GetDependentValueText(dependentValue)}.")
    {
        if (string.IsNullOrWhiteSpace(dependentProperty))
        {
            throw new ArgumentException("The dependent property name must be provided.", nameof(dependentProperty));
        }

        this.DependentProperty = dependentProperty;
        this.DependentValue = dependentValue;
        this.DependentValues = dependentValue is Array array ? array.Cast<object?>().ToArray() : [dependentValue];
        this._dependentValueTexts = [.. this.DependentValues.Select(static value => value?.ToString())];
    }

    /// <summary>
    /// Gets the sibling property whose value activates the prohibition.
    /// </summary>
    public string DependentProperty { get; }

    /// <summary>
    /// Gets the original dependent value supplied to the attribute constructor.
    /// </summary>
    public object? DependentValue { get; }

    /// <summary>
    /// Gets the dependent values normalised into an enumerable so arrays and scalar inputs follow the same validation path.
    /// </summary>
    public IEnumerable<object?> DependentValues { get; }

    /// <inheritdoc />
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Blueprint validation attributes inspect preserved public operation properties during runtime fallback validation.")]
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        ArgumentNullException.ThrowIfNull(validationContext);

        if (value is null)
        {
            return ValidationResult.Success;
        }

        var dependentProperty = CrossPropertyAccessorCache.GetProperty(validationContext.ObjectType, this.DependentProperty);
        if (dependentProperty is null)
        {
            return new ValidationResult(
                $"The property '{this.DependentProperty}' referenced by NotAllowedIfAttribute on '{validationContext.MemberName}' does not exist on '{validationContext.ObjectType.Name}'.",
                validationContext.MemberName is null ? null : [validationContext.MemberName]);
        }

        var dependentValue = dependentProperty.GetValue(validationContext.ObjectInstance);
        if (!this.MatchesDependentValue(dependentValue))
        {
            return ValidationResult.Success;
        }

        return new ValidationResult(
            this.FormatErrorMessage(validationContext.DisplayName),
            validationContext.MemberName is null ? null : [validationContext.MemberName]);
    }

    private bool MatchesDependentValue(object? candidate)
    {
        return this._dependentValueTexts.Contains(candidate?.ToString());
    }

    private static string GetDependentValueText(object? dependentValue)
    {
        if (dependentValue is Array array)
        {
            return string.Join(", ", array.Cast<object?>().Select(GetDependentValueText));
        }

        return dependentValue is null
            ? "null"
            : Convert.ToString(dependentValue, CultureInfo.CurrentCulture) ?? dependentValue.ToString() ?? "null";
    }
}

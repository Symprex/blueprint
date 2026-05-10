using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Blueprint.Validation;

/// <summary>
/// Requires a value only when another property matches one of the configured dependent values.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class RequiredIfAttribute : ValidationAttribute
{
    private const string RequiredFieldMessage = "The {0} field is required.";
    private readonly HashSet<string?> _dependentValueTexts;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequiredIfAttribute"/> class.
    /// </summary>
    /// <param name="dependentProperty">The sibling property whose value controls whether this member becomes required.</param>
    /// <param name="dependentValue">The dependent value, or values, that activate the requirement.</param>
    public RequiredIfAttribute(string dependentProperty, object? dependentValue)
        : base(RequiredFieldMessage)
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
    /// Gets the sibling property whose value controls whether this member becomes required.
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
    public override string FormatErrorMessage(string name)
    {
        return string.Format(CultureInfo.CurrentCulture, this.ErrorMessageString, name);
    }

    /// <inheritdoc />
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Blueprint validation attributes inspect preserved public operation properties during runtime fallback validation.")]
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        ArgumentNullException.ThrowIfNull(validationContext);

        var dependentGetter = CrossPropertyAccessorCache.GetGetter(validationContext.ObjectType, this.DependentProperty);
        if (dependentGetter is null)
        {
            return new ValidationResult(
                $"The property '{this.DependentProperty}' referenced by RequiredIfAttribute on '{validationContext.MemberName}' does not exist on '{validationContext.ObjectType.Name}'.",
                validationContext.MemberName is null ? null : [validationContext.MemberName]);
        }

        var dependentValue = dependentGetter(validationContext.ObjectInstance!);
        if (!this.MatchesDependentValue(dependentValue) || HasRequiredValue(value))
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

    private static bool HasRequiredValue(object? value)
    {
        return value switch
        {
            string stringValue => !string.IsNullOrWhiteSpace(stringValue),
            null => false,
            _ => true,
        };
    }
}

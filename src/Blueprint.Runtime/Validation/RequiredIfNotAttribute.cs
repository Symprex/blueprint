using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Blueprint.Validation;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class RequiredIfNotAttribute : ValidationAttribute
{
    private const string RequiredFieldMessage = "The {0} field is required.";
    private readonly HashSet<string?> _dependentValueTexts;

    public RequiredIfNotAttribute(string dependentProperty, object? dependentValue)
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

    public string DependentProperty { get; }

    public object? DependentValue { get; }

    public IEnumerable<object?> DependentValues { get; }

    public override string FormatErrorMessage(string name)
    {
        return string.Format(CultureInfo.CurrentCulture, this.ErrorMessageString, name);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Blueprint validation attributes inspect preserved public operation properties during runtime fallback validation.")]
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        ArgumentNullException.ThrowIfNull(validationContext);

        var dependentProperty = validationContext.ObjectType.GetProperty(this.DependentProperty);
        if (dependentProperty is null)
        {
            return new ValidationResult(
                $"The property '{this.DependentProperty}' referenced by RequiredIfNotAttribute on '{validationContext.MemberName}' does not exist on '{validationContext.ObjectType.Name}'.",
                validationContext.MemberName is null ? null : [validationContext.MemberName]);
        }

        var dependentValue = dependentProperty.GetValue(validationContext.ObjectInstance);
        if (this.MatchesDependentValue(dependentValue) || HasRequiredValue(value))
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

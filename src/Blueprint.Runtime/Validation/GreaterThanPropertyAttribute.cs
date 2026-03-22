using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Blueprint.Validation;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class GreaterThanPropertyAttribute : ValidationAttribute
{
    public GreaterThanPropertyAttribute(string otherPropertyName)
        : base("The field {0} must be greater than '{1}'.")
    {
        if (string.IsNullOrWhiteSpace(otherPropertyName))
        {
            throw new ArgumentException("The other property name must be provided.", nameof(otherPropertyName));
        }

        this.OtherPropertyName = otherPropertyName;
    }

    public string OtherPropertyName { get; }

    public override string FormatErrorMessage(string name)
    {
        return string.Format(CultureInfo.CurrentCulture, this.ErrorMessageString, name, this.OtherPropertyName);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Blueprint validation attributes inspect preserved public operation properties during runtime fallback validation.")]
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        ArgumentNullException.ThrowIfNull(validationContext);

        var otherProperty = validationContext.ObjectType.GetProperty(this.OtherPropertyName);
        if (otherProperty is null)
        {
            return new ValidationResult(
                $"The property '{this.OtherPropertyName}' referenced by GreaterThanPropertyAttribute on '{validationContext.MemberName}' does not exist on '{validationContext.ObjectType.Name}'.",
                validationContext.MemberName is null ? null : [validationContext.MemberName]);
        }

        var otherValue = otherProperty.GetValue(validationContext.ObjectInstance);
        if (value is null || otherValue is null)
        {
            return ValidationResult.Success;
        }

        if (value is IComparable comparable)
        {
            try
            {
                if (comparable.CompareTo(otherValue) > 0)
                {
                    return ValidationResult.Success;
                }
            }
            catch (ArgumentException)
            {
            }
        }

        return new ValidationResult(
            this.FormatErrorMessage(validationContext.DisplayName),
            validationContext.MemberName is null ? null : [validationContext.MemberName]);
    }
}

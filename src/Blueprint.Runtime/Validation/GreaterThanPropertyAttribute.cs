using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Blueprint.Validation;

/// <summary>
/// Requires a property value to compare greater than another property on the same object during runtime validation.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class GreaterThanPropertyAttribute : ValidationAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GreaterThanPropertyAttribute"/> class.
    /// </summary>
    /// <param name="otherPropertyName">The sibling property whose value acts as the comparison baseline.</param>
    public GreaterThanPropertyAttribute(string otherPropertyName)
        : base("The field {0} must be greater than '{1}'.")
    {
        if (string.IsNullOrWhiteSpace(otherPropertyName))
        {
            throw new ArgumentException("The other property name must be provided.", nameof(otherPropertyName));
        }

        this.OtherPropertyName = otherPropertyName;
    }

    /// <summary>
    /// Gets the sibling property name whose value acts as the comparison baseline.
    /// </summary>
    public string OtherPropertyName { get; }

    /// <inheritdoc />
    public override string FormatErrorMessage(string name)
    {
        return string.Format(CultureInfo.CurrentCulture, this.ErrorMessageString, name, this.OtherPropertyName);
    }

    /// <inheritdoc />
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Blueprint validation attributes inspect preserved public operation properties during runtime fallback validation.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Cross-property validation uses compiled expression delegates for property access. Validation attributes are inherently reflection-based.")]
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        ArgumentNullException.ThrowIfNull(validationContext);

        var otherGetter = CrossPropertyAccessorCache.GetGetter(validationContext.ObjectType, this.OtherPropertyName);
        if (otherGetter is null)
        {
            return new ValidationResult(
                $"The property '{this.OtherPropertyName}' referenced by GreaterThanPropertyAttribute on '{validationContext.MemberName}' does not exist on '{validationContext.ObjectType.Name}'.",
                validationContext.MemberName is null ? null : [validationContext.MemberName]);
        }

        var otherValue = otherGetter(validationContext.ObjectInstance!);
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

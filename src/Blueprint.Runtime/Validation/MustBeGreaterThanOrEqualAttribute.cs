using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Blueprint.Validation;

/// <summary>
/// Requires that a property's value is greater than or equal to the value of another property on the same object.
/// This is a cross-property comparison validator similar to <see cref="GreaterThanPropertyAttribute"/> but
/// with an inclusive lower bound.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MustBeGreaterThanOrEqualAttribute : ValidationAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MustBeGreaterThanOrEqualAttribute"/> class.
    /// </summary>
    /// <param name="dependentProperty">The sibling property whose value acts as the comparison baseline.</param>
    public MustBeGreaterThanOrEqualAttribute(string dependentProperty)
        : base("The {0} field must be greater than or equal to " + dependentProperty)
    {
        if (string.IsNullOrWhiteSpace(dependentProperty))
        {
            throw new ArgumentException("The dependent property name must be provided.", nameof(dependentProperty));
        }

        this.DependentProperty = dependentProperty;
    }

    /// <summary>
    /// Gets the sibling property name whose value is the comparison baseline.
    /// </summary>
    public string DependentProperty { get; }

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

        var getter = CrossPropertyAccessorCache.GetGetter(validationContext.ObjectType, this.DependentProperty);

        if (getter is null)
        {
            return new ValidationResult(
                $"The property '{this.DependentProperty}' referenced by MustBeGreaterThanOrEqualAttribute on '{validationContext.MemberName}' does not exist on '{validationContext.ObjectType.Name}'.",
                validationContext.MemberName is null ? null : [validationContext.MemberName]);
        }

        var otherValue = getter(validationContext.ObjectInstance!);

        if (value is null || otherValue is null)
        {
            return ValidationResult.Success;
        }

        if (otherValue is IComparable comparable && comparable.CompareTo(value) <= 0)
        {
            return ValidationResult.Success;
        }

        return new ValidationResult(
            this.FormatErrorMessage(validationContext.DisplayName),
            validationContext.MemberName is null ? null : [validationContext.MemberName]);
    }
}

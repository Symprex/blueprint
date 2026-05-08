using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Blueprint.Validation;

/// <summary>
/// Requires a numeric value to be less than a configured maximum while still producing DataAnnotations-compatible
/// validation messages.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property)]
public sealed class LessThanAttribute : ValidationAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LessThanAttribute"/> class using the default error message.
    /// </summary>
    /// <param name="maximumValue">The exclusive maximum value the annotated member must be below.</param>
    public LessThanAttribute(int maximumValue)
        : base("The field {0} must be less than '{1}'.")
    {
        this.MaximumValue = maximumValue;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LessThanAttribute"/> class using a custom error message.
    /// </summary>
    /// <param name="maximumValue">The exclusive maximum value the annotated member must be below.</param>
    /// <param name="errorMessage">The custom error message template.</param>
    public LessThanAttribute(int maximumValue, string errorMessage)
        : base(errorMessage)
    {
        this.MaximumValue = maximumValue;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LessThanAttribute"/> class using a lazily-evaluated error message.
    /// </summary>
    /// <param name="maximumValue">The exclusive maximum value the annotated member must be below.</param>
    /// <param name="errorMessageAccessor">The callback that provides the error message template.</param>
    public LessThanAttribute(int maximumValue, Func<string> errorMessageAccessor)
        : base(errorMessageAccessor)
    {
        this.MaximumValue = maximumValue;
    }

    /// <summary>
    /// Gets the exclusive maximum value that the annotated member must be below.
    /// </summary>
    public int MaximumValue { get; }

    /// <inheritdoc />
    public override string FormatErrorMessage(string name)
    {
        return string.Format(CultureInfo.CurrentCulture, this.ErrorMessageString, name, this.MaximumValue);
    }

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            return true;
        }

        var valueText = Convert.ToString(value, CultureInfo.CurrentCulture);
        return decimal.TryParse(valueText, NumberStyles.Number, CultureInfo.CurrentCulture, out var numericValue)
               && numericValue < this.MaximumValue;
    }
}

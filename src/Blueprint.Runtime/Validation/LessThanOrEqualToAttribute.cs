using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Blueprint.Validation;

/// <summary>
/// Requires a numeric value to be less than or equal to a configured maximum while still producing
/// DataAnnotations-compatible validation messages.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property)]
public sealed class LessThanOrEqualToAttribute : ValidationAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LessThanOrEqualToAttribute"/> class using the default error message.
    /// </summary>
    /// <param name="maximumValue">The inclusive maximum value the annotated member must not exceed.</param>
    public LessThanOrEqualToAttribute(int maximumValue)
        : base("The field {0} must be less than or equal to '{1}'.")
    {
        this.MaximumValue = maximumValue;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LessThanOrEqualToAttribute"/> class using a custom error message.
    /// </summary>
    /// <param name="maximumValue">The inclusive maximum value the annotated member must not exceed.</param>
    /// <param name="errorMessage">The custom error message template.</param>
    public LessThanOrEqualToAttribute(int maximumValue, string errorMessage)
        : base(errorMessage)
    {
        this.MaximumValue = maximumValue;
    }

    /// <summary>
    /// Gets the inclusive maximum value that the annotated member must not exceed.
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
               && numericValue <= this.MaximumValue;
    }
}

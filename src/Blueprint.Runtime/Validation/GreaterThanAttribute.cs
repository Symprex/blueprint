using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Blueprint.Validation;

/// <summary>
/// Requires a numeric value to be greater than a configured minimum while still producing DataAnnotations-compatible
/// validation messages.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property)]
public sealed class GreaterThanAttribute : ValidationAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GreaterThanAttribute"/> class using the default error message.
    /// </summary>
    /// <param name="minimumValue">The exclusive minimum value the annotated member must exceed.</param>
    public GreaterThanAttribute(int minimumValue)
        : base("The field {0} must be greater than '{1}'.")
    {
        this.MinimumValue = minimumValue;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GreaterThanAttribute"/> class using a custom error message.
    /// </summary>
    /// <param name="minimumValue">The exclusive minimum value the annotated member must exceed.</param>
    /// <param name="errorMessage">The custom error message template.</param>
    public GreaterThanAttribute(int minimumValue, string errorMessage)
        : base(errorMessage)
    {
        this.MinimumValue = minimumValue;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GreaterThanAttribute"/> class using a lazily-evaluated error message.
    /// </summary>
    /// <param name="minimumValue">The exclusive minimum value the annotated member must exceed.</param>
    /// <param name="errorMessageAccessor">The callback that provides the error message template.</param>
    public GreaterThanAttribute(int minimumValue, Func<string> errorMessageAccessor)
        : base(errorMessageAccessor)
    {
        this.MinimumValue = minimumValue;
    }

    /// <summary>
    /// Gets the exclusive minimum value that the annotated member must exceed.
    /// </summary>
    public int MinimumValue { get; }

    /// <inheritdoc />
    public override string FormatErrorMessage(string name)
    {
        return string.Format(CultureInfo.CurrentCulture, this.ErrorMessageString, name, this.MinimumValue);
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
               && numericValue > this.MinimumValue;
    }
}

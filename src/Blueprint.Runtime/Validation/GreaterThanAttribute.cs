using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Blueprint.Validation;

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property)]
public sealed class GreaterThanAttribute : ValidationAttribute
{
    public GreaterThanAttribute(int minimumValue)
        : base("The field {0} must be greater than '{1}'.")
    {
        this.MinimumValue = minimumValue;
    }

    public GreaterThanAttribute(int minimumValue, string errorMessage)
        : base(errorMessage)
    {
        this.MinimumValue = minimumValue;
    }

    public GreaterThanAttribute(int minimumValue, Func<string> errorMessageAccessor)
        : base(errorMessageAccessor)
    {
        this.MinimumValue = minimumValue;
    }

    public int MinimumValue { get; }

    public override string FormatErrorMessage(string name)
    {
        return string.Format(CultureInfo.CurrentCulture, this.ErrorMessageString, name, this.MinimumValue);
    }

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

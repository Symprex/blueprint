using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Blueprint.Validation;

/// <summary>
/// Validates that a <see cref="DateTime"/> or <see cref="DateTimeOffset"/> value is in the future
/// (strictly after the current UTC time).
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property)]
public sealed class InFutureAttribute : ValidationAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InFutureAttribute"/> class with the default error message.
    /// </summary>
    public InFutureAttribute()
        : base("The {0} field must be a date in the future.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InFutureAttribute"/> class with a custom error message.
    /// </summary>
    /// <param name="errorMessage">The custom error message template.</param>
    public InFutureAttribute(string errorMessage)
        : base(errorMessage)
    {
    }

    /// <inheritdoc />
    public override string FormatErrorMessage(string name)
    {
        return string.Format(CultureInfo.CurrentCulture, this.ErrorMessageString, name);
    }

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        return value switch
        {
            null => true,
            DateTime dateTime => dateTime > DateTime.UtcNow,
            DateTimeOffset dateTimeOffset => dateTimeOffset > DateTimeOffset.UtcNow,
            _ => false,
        };
    }
}

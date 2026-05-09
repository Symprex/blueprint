using Blueprint.Utilities;

namespace Blueprint.Validation;

/// <summary>
/// Validates that a string property matches a common phone number format using
/// <see cref="CommonRegularExpressions.PhoneNumberOnly"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property)]
public sealed class PhoneNumberAttribute : RegexAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PhoneNumberAttribute"/> class with the default error message.
    /// </summary>
    public PhoneNumberAttribute()
        : base(CommonRegularExpressions.PhoneNumberOnly, "Please enter a valid phone number")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PhoneNumberAttribute"/> class with a custom error message.
    /// </summary>
    /// <param name="errorMessage">The custom error message template.</param>
    public PhoneNumberAttribute(string errorMessage)
        : base(CommonRegularExpressions.PhoneNumberOnly, errorMessage)
    {
    }
}

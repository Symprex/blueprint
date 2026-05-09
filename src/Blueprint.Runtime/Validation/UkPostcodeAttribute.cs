using Blueprint.Utilities;

namespace Blueprint.Validation;

/// <summary>
/// Validates that a string property matches a UK postcode format using
/// <see cref="CommonRegularExpressions.UkPostcodeOnly"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property)]
public sealed class UkPostcodeAttribute : RegexAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UkPostcodeAttribute"/> class with the default error message.
    /// </summary>
    public UkPostcodeAttribute()
        : base(CommonRegularExpressions.UkPostcodeOnly, "Please enter a valid UK postcode")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UkPostcodeAttribute"/> class with a custom error message.
    /// </summary>
    /// <param name="errorMessage">The custom error message template.</param>
    public UkPostcodeAttribute(string errorMessage)
        : base(CommonRegularExpressions.UkPostcodeOnly, errorMessage)
    {
    }
}

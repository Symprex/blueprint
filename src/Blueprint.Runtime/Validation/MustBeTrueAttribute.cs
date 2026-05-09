using System.ComponentModel.DataAnnotations;

namespace Blueprint.Validation;

/// <summary>
/// Validates that a <see cref="bool"/> property is <c>true</c>, typically used for consent or
/// terms-of-service checkboxes where the user must explicitly agree.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property)]
public sealed class MustBeTrueAttribute : ValidationAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MustBeTrueAttribute"/> class with the default error message.
    /// </summary>
    public MustBeTrueAttribute()
        : base("The {0} field must be checked.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MustBeTrueAttribute"/> class with a custom error message.
    /// </summary>
    /// <param name="errorMessage">The custom error message template.</param>
    public MustBeTrueAttribute(string errorMessage)
        : base(errorMessage)
    {
    }

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        return value is true;
    }
}

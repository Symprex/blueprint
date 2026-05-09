using System.Text.RegularExpressions;

namespace Blueprint.Validation;

/// <summary>
/// Requires that a string meets strong password complexity rules: at least one lowercase letter,
/// one uppercase letter, one digit, one special character, and a minimum length of 8 characters.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property)]
public sealed class PasswordAttribute : RegexAttribute
{
    private const string DefaultMessage =
        "A password must contain at least: 1 lower case letter, 1 upper case letter, 1 number, 1 special character and be at least 8 characters long.";

    private static readonly Regex StrongPassword =
        new(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z\d]).{8,}$", RegexOptions.Compiled);

    /// <summary>
    /// Initializes a new instance of the <see cref="PasswordAttribute"/> class with the default error message.
    /// </summary>
    public PasswordAttribute()
        : base(StrongPassword, DefaultMessage)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PasswordAttribute"/> class with a custom error message.
    /// </summary>
    /// <param name="errorMessage">The error message to display on validation failure.</param>
    public PasswordAttribute(string errorMessage)
        : base(StrongPassword, errorMessage)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PasswordAttribute"/> class with a custom error message
    /// and optional appended default description.
    /// </summary>
    /// <param name="errorMessage">The error message to display on validation failure.</param>
    /// <param name="appendInvalidPasswordDescription">When <c>true</c>, appends the default password rules to the error message.</param>
    public PasswordAttribute(string errorMessage, bool appendInvalidPasswordDescription)
        : base(StrongPassword, appendInvalidPasswordDescription ? errorMessage + " " + DefaultMessage : errorMessage)
    {
    }
}

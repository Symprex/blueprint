using System.Diagnostics.CodeAnalysis;
using Blueprint.Utilities;

namespace Blueprint.Validation;

/// <summary>
/// Validates that a property value is a well-formed email address using
/// <see cref="CommonRegularExpressions.EmailOnly"/>.
/// </summary>
/// <remarks>
/// Empty or null values pass validation (use <c>[Required]</c> to enforce presence).
/// This is intentionally permissive — only sending an email can truly validate an address.
/// </remarks>
[SuppressMessage(
    "Microsoft.Design",
    "CA1019:DefineAccessorsForAttributeArguments",
    Justification = "errorMessageAccessor is defined in base class, not accessible")]
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property)]
public sealed class EmailAddressAttribute : RegexAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EmailAddressAttribute"/> class with the
    /// default error message.
    /// </summary>
    public EmailAddressAttribute()
        : base(CommonRegularExpressions.EmailOnly, "Please enter a valid email address")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailAddressAttribute"/> class with a
    /// custom error message.
    /// </summary>
    /// <param name="errorMessage">The error message shown on validation failure.</param>
    public EmailAddressAttribute(string errorMessage)
        : base(CommonRegularExpressions.EmailOnly, errorMessage)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailAddressAttribute"/> class with a
    /// deferred error message accessor.
    /// </summary>
    /// <param name="errorMessageAccessor">A function returning the error message.</param>
    public EmailAddressAttribute(Func<string> errorMessageAccessor)
        : base(CommonRegularExpressions.EmailOnly, errorMessageAccessor)
    {
    }
}

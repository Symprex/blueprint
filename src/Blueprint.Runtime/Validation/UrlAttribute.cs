using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Blueprint.Utilities;

namespace Blueprint.Validation;

/// <summary>
/// Validates that a property value is a well-formed URL using
/// <see cref="CommonRegularExpressions.UrlOnly"/> or
/// <see cref="CommonRegularExpressions.UrlWithProtocol"/> depending on the
/// <see cref="UrlProtocol"/> setting.
/// </summary>
/// <remarks>
/// Empty or null values pass validation (use <c>[Required]</c> to enforce presence).
/// </remarks>
[SuppressMessage(
    "Microsoft.Design",
    "CA1019:DefineAccessorsForAttributeArguments",
    Justification = "errorMessageAccessor is defined in base class, not accessible")]
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property)]
public sealed class UrlAttribute : RegexAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UrlAttribute"/> class.
    /// </summary>
    /// <param name="urlProtocol">Whether the protocol prefix is required.</param>
    public UrlAttribute(UrlProtocol urlProtocol = UrlProtocol.NotRequired)
        : base(GetUrlProtocolRegexVariant(urlProtocol), GetUrlMessage(urlProtocol))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UrlAttribute"/> class with a custom
    /// error message.
    /// </summary>
    /// <param name="errorMessage">The error message shown on validation failure.</param>
    /// <param name="urlProtocol">Whether the protocol prefix is required.</param>
    public UrlAttribute(string errorMessage, UrlProtocol urlProtocol = UrlProtocol.NotRequired)
        : base(GetUrlProtocolRegexVariant(urlProtocol), errorMessage)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UrlAttribute"/> class with a deferred
    /// error message accessor.
    /// </summary>
    /// <param name="errorMessageAccessor">A function returning the error message.</param>
    /// <param name="urlProtocol">Whether the protocol prefix is required.</param>
    public UrlAttribute(Func<string> errorMessageAccessor, UrlProtocol urlProtocol = UrlProtocol.NotRequired)
        : base(GetUrlProtocolRegexVariant(urlProtocol), errorMessageAccessor)
    {
    }

    private static Regex GetUrlProtocolRegexVariant(UrlProtocol urlProtocol)
    {
        return urlProtocol == UrlProtocol.NotRequired
            ? CommonRegularExpressions.UrlOnly
            : CommonRegularExpressions.UrlWithProtocol;
    }

    private static string GetUrlMessage(UrlProtocol urlProtocol)
    {
        return urlProtocol == UrlProtocol.NotRequired
            ? "Please enter a valid url"
            : "Please enter a fully qualified url";
    }
}

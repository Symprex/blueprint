using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Blueprint.Validation;

/// <summary>
/// Base class for validation attributes that use a pre-compiled <see cref="Regex" /> for
/// validation rather than a string pattern. Subclass this when creating domain-specific
/// regex validators (e.g. <see cref="EmailAddressAttribute"/>, <see cref="UrlAttribute"/>).
/// </summary>
/// <remarks>
/// The standard <see cref="RegularExpressionAttribute" /> only accepts a string pattern,
/// which means <see cref="RegexOptions"/> cannot be specified without inline modifiers.
/// This class accepts a pre-compiled <see cref="Regex"/> instance for full control.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public abstract class RegexAttribute : ValidationAttribute
{
    private readonly Regex _regex;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegexAttribute"/> class with the
    /// specified regular expression.
    /// </summary>
    /// <param name="regex">The compiled regular expression to validate against.</param>
    protected RegexAttribute(Regex regex)
    {
        this._regex = regex;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RegexAttribute"/> class with the
    /// specified regular expression and error message.
    /// </summary>
    /// <param name="regex">The compiled regular expression to validate against.</param>
    /// <param name="errorMessage">The error message shown on validation failure.</param>
    protected RegexAttribute(Regex regex, string errorMessage)
        : base(errorMessage)
    {
        this._regex = regex;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RegexAttribute"/> class with the
    /// specified regular expression and a deferred error message accessor.
    /// </summary>
    /// <param name="regex">The compiled regular expression to validate against.</param>
    /// <param name="errorMessageAccessor">A function returning the error message on failure.</param>
    protected RegexAttribute(Regex regex, Func<string> errorMessageAccessor)
        : base(errorMessageAccessor)
    {
        this._regex = regex;
    }

    /// <summary>
    /// Gets the compiled regular expression used by this validation attribute.
    /// </summary>
    public Regex Regex => this._regex;

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        var input = Convert.ToString(value, CultureInfo.CurrentCulture);

        if (string.IsNullOrEmpty(input))
        {
            return true;
        }

        var match = this._regex.Match(input);

        return match.Success && match.Index == 0 && match.Length == input.Length;
    }

    /// <inheritdoc />
    public override string FormatErrorMessage(string name)
    {
        return string.Format(
            CultureInfo.CurrentCulture,
            this.ErrorMessageString,
            name,
            this._regex.ToString());
    }
}

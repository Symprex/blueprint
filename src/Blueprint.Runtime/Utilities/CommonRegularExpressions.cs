using System.Text.RegularExpressions;

namespace Blueprint.Utilities;

/// <summary>
/// Pre-compiled regular expressions for common validation scenarios such as email addresses,
/// phone numbers, postcodes, and URLs. Consumer code can reference these instead of
/// duplicating patterns across projects.
/// </summary>
public static partial class CommonRegularExpressions
{
    /// <summary>
    /// A pattern that matches the local-part and domain of an email address.
    /// This is intentionally permissive: it catches the majority of bad addresses but
    /// cannot guarantee correctness — only sending an email can do that.
    /// </summary>
    public const string EmailPattern =
        @"[a-zA-Z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[a-zA-Z0-9!#$%&'*+/=?^_`{|}~-]+)*" +
        "@" +
        @"(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?\.)+[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?";

    /// <summary>
    /// A pattern that matches common phone number formats (digits, spaces, dots,
    /// parentheses, plus sign, <c>x</c> for extensions, and hyphens).
    /// </summary>
    public const string PhoneNumberPattern = @"[\d .()+x-]{1,30}";

    /// <summary>
    /// A pattern that matches UK postcodes following Royal Mail formatting rules.
    /// A matching string is not guaranteed to be a real postcode.
    /// </summary>
    public const string UkPostcodePattern =
        @"([Gg][Ii][Rr] ?0[aA]{2})|((([a-zA-Z][0-9]{1,2})|(([a-zA-Z][A-HJ-Ya-hj-y][0-9]{1,2})|(([a-zA-Z][0-9][a-zA-Z])|([a-zA-Z][A-HJ-Ya-hj-y][0-9]?[a-zA-Z])))) ?[0-9][a-zA-Z]{2})";

    /// <summary>
    /// A pattern that matches a URL with an optional protocol prefix.
    /// </summary>
    public const string UrlPattern = UrlProtocolPattern + "?" + UrlDomainPattern;

    /// <summary>
    /// Finds email addresses within a larger string using word boundaries.
    /// </summary>
    public static readonly Regex Email = CreateEmailRegex();

    /// <summary>
    /// Matches a string that contains only an email address (with optional surrounding whitespace).
    /// </summary>
    public static readonly Regex EmailOnly = CreateEmailOnlyRegex();

    /// <summary>
    /// Finds phone numbers within a larger string using word boundaries.
    /// </summary>
    public static readonly Regex PhoneNumber = CreatePhoneNumberRegex();

    /// <summary>
    /// Matches a string that contains only a phone number.
    /// </summary>
    public static readonly Regex PhoneNumberOnly = CreatePhoneNumberOnlyRegex();

    /// <summary>
    /// Finds UK postcodes within a larger string using word boundaries.
    /// </summary>
    public static readonly Regex UkPostcode = CreateUkPostcodeRegex();

    /// <summary>
    /// Matches a string that contains only a UK postcode.
    /// </summary>
    public static readonly Regex UkPostcodeOnly = CreateUkPostcodeOnlyRegex();

    /// <summary>
    /// Finds URLs within a larger string using word boundaries.
    /// </summary>
    public static readonly Regex Url = CreateUrlRegex();

    /// <summary>
    /// Matches a string that contains only a URL (protocol optional).
    /// </summary>
    public static readonly Regex UrlOnly = CreateUrlOnlyRegex();

    /// <summary>
    /// Matches a string that contains only a URL where the protocol prefix is mandatory.
    /// </summary>
    public static readonly Regex UrlWithProtocol = CreateUrlWithProtocolRegex();

    private const string UrlProtocolPattern = @"((([Hh][Tt]|[Ff])[Tt][Pp]([Ss]?))\://)";
    private const string UrlWithProtocolPattern = UrlProtocolPattern + UrlDomainPattern;
    private const string UrlDomainPattern = @"([a-zA-Z0-9\-\.]+\.[a-zA-Z]{2,63}\.?|localhost)(\:[0-9]{1,5})*([/?]($|[a-zA-Z0-9\.\,\;\'\\\+&amp;()%@\$#\=~_\-]+))*";

    // Factory methods wrapping compiled Regex instances. These cannot use GeneratedRegex
    // because the patterns are built from const string concatenation with interpolation,
    // which the source generator does not support.

    private static Regex CreateEmailRegex()
    {
        return new($@"\b{EmailPattern}\b", RegexOptions.Compiled);
    }

    private static Regex CreateEmailOnlyRegex()
    {
        return new($@"^\s*{EmailPattern}\s*$", RegexOptions.Compiled);
    }

    private static Regex CreatePhoneNumberRegex()
    {
        return new($@"\b{PhoneNumberPattern}\b", RegexOptions.Compiled);
    }

    private static Regex CreatePhoneNumberOnlyRegex()
    {
        return new($@"^\s*{PhoneNumberPattern}\s*$", RegexOptions.Compiled);
    }

    private static Regex CreateUkPostcodeRegex()
    {
        return new($@"\b{UkPostcodePattern}\b", RegexOptions.Compiled);
    }

    private static Regex CreateUkPostcodeOnlyRegex()
    {
        return new($@"^\s*{UkPostcodePattern}\s*$", RegexOptions.Compiled);
    }

    private static Regex CreateUrlRegex()
    {
        return new($@"\b{UrlPattern}\b", RegexOptions.Compiled);
    }

    private static Regex CreateUrlOnlyRegex()
    {
        return new($@"^\s*{UrlPattern}\s*$", RegexOptions.Compiled);
    }

    private static Regex CreateUrlWithProtocolRegex()
    {
        return new($@"^\s*{UrlWithProtocolPattern}\s*$", RegexOptions.Compiled);
    }
}

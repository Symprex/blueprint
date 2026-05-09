using System.Text;
using System.Text.RegularExpressions;

using JetBrains.Annotations;

namespace Blueprint.Utilities;

/// <summary>
/// Extension methods for <see cref="string"/> that provide common casing transformations
/// used in API resource naming, property mapping, and code generation scenarios.
/// </summary>
[PublicAPI]
public static partial class StringExtensions
{
    /// <summary>
    /// Converts an underscore-separated or lowercase string to PascalCase by capitalising the
    /// first letter of each segment delimited by underscores.
    /// </summary>
    /// <remarks>
    /// This is the inflector-style conversion: <c>"order_id"</c> → <c>"OrderId"</c>,
    /// <c>"first_name"</c> → <c>"FirstName"</c>. For broader separator handling
    /// (spaces, dashes, digits) use <see cref="ToPascalCase"/>.
    /// </remarks>
    /// <param name="lowercaseAndUnderscoredWord">The underscore-separated string to convert.</param>
    /// <returns>The PascalCased representation.</returns>
    public static string Pascalize(this string lowercaseAndUnderscoredWord)
    {
        return PascalizeRegex().Replace(
            lowercaseAndUnderscoredWord,
            static match => match.Groups[1].Value.ToUpper());
    }

    /// <summary>
    /// Converts a string to PascalCase by capitalising the first letter of each word and
    /// removing separators such as spaces, dashes, and underscores.
    /// </summary>
    /// <param name="s">The input string to convert.</param>
    /// <returns>The PascalCased representation.</returns>
    public static string ToPascalCase(this string s)
    {
        var sb = new StringBuilder(s.Length);
        var toUpper = true;

        for (var i = 0; i < s.Length; i++)
        {
            var curChar = s[i];

            switch (curChar)
            {
                case ' ':
                case '-':
                case '_':
                    toUpper = true;
                    continue;

                case >= '0' and <= '9':
                    toUpper = true;
                    sb.Append(curChar);
                    continue;

                default:
                    if (toUpper)
                    {
                        sb.Append(char.ToUpper(curChar));
                        toUpper = false;
                    }
                    else
                    {
                        var nextIsUpper = i < s.Length - 1 && char.IsLower(s[i + 1]);
                        var prevIsLower = i > 0 && char.IsLower(s[i - 1]);

                        if (nextIsUpper)
                        {
                            sb.Append(curChar);
                        }
                        else if (prevIsLower && curChar is 'A' or 'I')
                        {
                            sb.Append(curChar);
                        }
                        else
                        {
                            sb.Append(char.ToLower(curChar));
                        }
                    }

                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Converts a string to camelCase by lowercasing the first character and capitalising the
    /// first letter of subsequent words, removing separators such as spaces, dashes, and underscores.
    /// </summary>
    /// <param name="s">The input string to convert.</param>
    /// <returns>The camelCased representation.</returns>
    public static string ToCamelCase(this string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return s;
        }

        var sb = new StringBuilder(s.Length);
        var toUpper = false;
        var toLower = true;

        for (var i = 0; i < s.Length; i++)
        {
            var curChar = s[i];

            switch (curChar)
            {
                case ' ':
                case '-':
                case '_':
                    toUpper = true;
                    continue;

                case >= '0' and <= '9':
                    sb.Append(curChar);
                    toUpper = true;
                    continue;

                default:
                    if (toLower)
                    {
                        sb.Append(char.ToLower(curChar));
                        toLower = false;
                    }
                    else if (toUpper)
                    {
                        sb.Append(char.ToUpper(curChar));
                        toUpper = false;
                    }
                    else
                    {
                        sb.Append(curChar);
                    }

                    break;
            }
        }

        return sb.ToString();
    }

    [GeneratedRegex("(?:^|_)(.)", RegexOptions.Compiled)]
    private static partial Regex PascalizeRegex();
}

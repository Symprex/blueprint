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

    /// <summary>
    /// Converts an underscore-separated string to camelCase by PascalCasing and then lowering
    /// the first character.
    /// </summary>
    /// <remarks>
    /// This is the inflector-style conversion: <c>"order_id"</c> → <c>"orderId"</c>,
    /// <c>"first_name"</c> → <c>"firstName"</c>.
    /// </remarks>
    /// <param name="lowercaseAndUnderscoredWord">The underscore-separated string to convert.</param>
    /// <returns>The camelCased representation.</returns>
    public static string Camelize(this string lowercaseAndUnderscoredWord)
    {
        return Uncapitalize(Pascalize(lowercaseAndUnderscoredWord));
    }

    /// <summary>
    /// Lowercases the first character of a string, leaving the rest unchanged.
    /// </summary>
    /// <param name="word">The word to uncapitalize.</param>
    /// <returns>The string with a lowercase first character.</returns>
    public static string Uncapitalize(this string word)
    {
        if (string.IsNullOrEmpty(word))
        {
            return word;
        }

        if (char.IsLower(word[0]))
        {
            return word;
        }

        return string.Create(word.Length, word, static (span, original) =>
        {
            original.AsSpan().CopyTo(span);
            span[0] = char.ToLowerInvariant(span[0]);
        });
    }

    /// <summary>
    /// Applies English pluralization rules to the given word, handling regular cases
    /// (adding -s, -es, -ies) and common irregular nouns (person→people, child→children, etc.).
    /// </summary>
    /// <param name="word">The singular word to pluralize.</param>
    /// <returns>The pluralized form of the word.</returns>
    public static string Pluralize(this string word)
    {
        return Inflector.Pluralize(word);
    }

    [GeneratedRegex("(?:^|_)(.)")]
    private static partial Regex PascalizeRegex();
}

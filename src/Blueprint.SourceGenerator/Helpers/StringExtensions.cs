using System.Text.RegularExpressions;

namespace Blueprint.SourceGenerator.Helpers;

/// <summary>
/// Provides identifier-shaping helpers used when generated source needs predictable naming conventions.
/// </summary>
public static class StringExtensions
{
    private static readonly Regex _pascalizeUnderscores = new("(?:^|_)(.)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Names a resource by legacy Blueprint's rule (Signature365 <c>origin/master</c>, <c>Blueprint.Http/ApiResource.cs:68-77</c>),
    /// which <c>ApiResource.GetTypeName</c> follows at run time: removes <c>ApiResource</c>, then <c>Resource</c>, then
    /// <c>DTO</c>, each wherever it occurs (ordinal, case-sensitive), and camelizes what is left with
    /// <see cref="LegacyCamelize"/>. A name that is nothing but markers is camelized as it is.
    /// </summary>
    /// <param name="className">The resource class's name.</param>
    /// <returns>The resource's object name.</returns>
    public static string ToLegacyResourceTypeName(this string className)
    {
        var name = className
            .Replace("ApiResource", string.Empty)
            .Replace("Resource", string.Empty)
            .Replace("DTO", string.Empty);

        return LegacyCamelize(name.Length > 0 ? name : className);
    }

    /// <summary>
    /// Camelizes as legacy Blueprint's <c>Camelize</c> (Inflector) did: raises the character at the start and after each
    /// underscore, removing the underscore, then lowers the first character only, so an acronym is kept but for its first
    /// letter (<c>SMSMessage</c> is <c>sMSMessage</c>). Unlike <see cref="Camelize"/>, which lowers a leading acronym.
    /// </summary>
    /// <param name="word">The text to camelize.</param>
    /// <returns>The camelized text, or the text itself when it is empty.</returns>
    public static string LegacyCamelize(this string word)
    {
        if (string.IsNullOrEmpty(word))
        {
            return word;
        }

        var pascal = _pascalizeUnderscores.Replace(word, static match => match.Groups[1].Value.ToUpperInvariant());

        return char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);
    }

    /// <summary>
    /// Converts a PascalCase identifier into a camelCase form while preserving common all-caps acronym prefixes.
    /// </summary>
    /// <param name="str">The identifier text to convert.</param>
    /// <returns>The camelCase identifier, or the original text when no leading-case change is required.</returns>
    public static string Camelize(this string str)
    {
        if (string.IsNullOrEmpty(str) || !char.IsUpper(str[0]))
        {
            return str;
        }

        var chars = str.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (i == 1 && !char.IsUpper(chars[i]))
            {
                break;
            }

            var hasNext = i + 1 < chars.Length;
            if (i > 0 && hasNext && !char.IsUpper(chars[i + 1]))
            {
                break;
            }

            chars[i] = char.ToLowerInvariant(chars[i]);
        }

        return new string(chars);
    }
}

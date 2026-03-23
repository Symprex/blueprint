namespace Blueprint.SourceGenerator.Helpers;

/// <summary>
/// Provides identifier-shaping helpers used when generated source needs predictable naming conventions.
/// </summary>
public static class StringExtensions
{
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

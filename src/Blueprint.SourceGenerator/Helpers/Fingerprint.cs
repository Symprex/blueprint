using System.Globalization;
using System.Text;

namespace Blueprint.SourceGenerator.Helpers;

/// <summary>
/// Joins values into one string for an incremental key, so that different values never join alike.
/// </summary>
internal static class Fingerprint
{
    /// <summary>
    /// Joins <paramref name="values"/>, each prefixed with its length (<c>-</c> for <see langword="null"/>): a delimiter
    /// alone would let <c>("a|b", "c")</c> and <c>("a", "b|c")</c> collide.
    /// </summary>
    /// <param name="values">The values, any of which may be <see langword="null"/>.</param>
    /// <returns>The joined fingerprint.</returns>
    public static string Join(params string?[] values)
    {
        var builder = new StringBuilder();

        foreach (var value in values)
        {
            if (value is null)
            {
                builder.Append("-;");
            }
            else
            {
                builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append(';');
            }
        }

        return builder.ToString();
    }
}

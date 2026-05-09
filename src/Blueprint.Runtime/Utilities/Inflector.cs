using System.Text.RegularExpressions;

using JetBrains.Annotations;

namespace Blueprint.Utilities;

/// <summary>
/// English-language inflection helpers for pluralizing words, ported from the
/// well-known Inflector library. Handles regular plural rules (adding -s, -es, -ies)
/// and a curated set of irregular nouns (person→people, child→children, etc.).
/// </summary>
[PublicAPI]
public static class Inflector
{
    private static readonly List<Rule> Plurals = new();
    private static readonly HashSet<string> Uncountables = new(StringComparer.OrdinalIgnoreCase);

    static Inflector()
    {
        AddPlural("$", "s");
        AddPlural("s$", "s");
        AddPlural("(ax|test)is$", "$1es");
        AddPlural("(octop|vir|alumn|fung)us$", "$1i");
        AddPlural("(alias|status)$", "$1es");
        AddPlural("(bu)s$", "$1ses");
        AddPlural("(buffal|tomat|volcan)o$", "$1oes");
        AddPlural("([ti])um$", "$1a");
        AddPlural("sis$", "ses");
        AddPlural("(?:([^f])fe|([lr])f)$", "$1$2ves");
        AddPlural("(hive)$", "$1s");
        AddPlural("([^aeiouy]|qu)y$", "$1ies");
        AddPlural("(x|ch|ss|sh)$", "$1es");
        AddPlural("(matr|vert|ind)ix|ex$", "$1ices");
        AddPlural("([m|l])ouse$", "$1ice");
        AddPlural("^(ox)$", "$1en");
        AddPlural("(quiz)$", "$1zes");

        AddIrregular("person", "people");
        AddIrregular("man", "men");
        AddIrregular("child", "children");
        AddIrregular("sex", "sexes");
        AddIrregular("move", "moves");
        AddIrregular("goose", "geese");
        AddIrregular("alumna", "alumnae");

        Uncountables.Add("equipment");
        Uncountables.Add("information");
        Uncountables.Add("rice");
        Uncountables.Add("money");
        Uncountables.Add("species");
        Uncountables.Add("series");
        Uncountables.Add("fish");
        Uncountables.Add("sheep");
        Uncountables.Add("deer");
        Uncountables.Add("aircraft");
    }

    /// <summary>
    /// Returns the plural form of the given English word. Handles regular cases
    /// (e.g. "user"→"users", "query"→"queries") and common irregulars
    /// (e.g. "person"→"people", "child"→"children"). Uncountable words
    /// (e.g. "sheep", "fish") are returned unchanged.
    /// </summary>
    /// <param name="word">The singular word to pluralize.</param>
    /// <returns>The plural form.</returns>
    public static string Pluralize(string word)
    {
        if (Uncountables.Contains(word))
        {
            return word;
        }

        for (var i = Plurals.Count - 1; i >= 0; i--)
        {
            var result = Plurals[i].Apply(word);
            if (result != null)
            {
                return result;
            }
        }

        return word;
    }

    private static void AddIrregular(string singular, string plural)
    {
        AddPlural("(" + singular[0] + ")" + singular[1..] + "$", "$1" + plural[1..]);
    }

    private static void AddPlural(string rule, string replacement)
    {
        Plurals.Add(new Rule(rule, replacement));
    }

    private sealed class Rule
    {
        private readonly Regex _regex;
        private readonly string _replacement;

        public Rule(string pattern, string replacement)
        {
            this._regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            this._replacement = replacement;
        }

        public string? Apply(string word)
        {
            if (!this._regex.IsMatch(word))
            {
                return null;
            }

            return this._regex.Replace(word, this._replacement);
        }
    }
}

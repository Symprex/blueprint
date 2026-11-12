using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Helpers;

/// <summary>
/// Reads the names of the operation properties a declared route template's placeholders bind, for every module that
/// decides from the raw template which properties the route owns.
/// </summary>
/// <remarks>
/// <para>
/// A placeholder is <c>{Name}</c>, <c>{*Name}</c> or <c>{**Name}</c>, optionally followed by legacy Blueprint's alternate
/// resource property, <c>{Name:ResourceProperty}</c>, or format, <c>{Name(format)}</c>, or both (Signature365
/// <c>8b40ecc961</c>, <c>Blueprint/ApiOperationLink.cs:29</c>). Legacy routed every such placeholder as <c>{Name}</c>
/// (<c>:53-78</c>) and bound <c>Name</c> from that route value (<c>Blueprint.Http/MessagePopulation/
/// HttpRouteMessagePopulationSource.cs:36-41</c>, <c>:99-107</c>); the suffix only told link generation which resource
/// property to read, and how to format it. So the bound name is the one before any <c>:</c> or <c>(</c>. That is also the
/// parameter name of an ASP.NET inline constraint, such as <c>{**path:required}</c>.
/// </para>
/// <para>
/// ASP.NET's optional marker, <c>{Ext?}</c>, and default value, <c>{Kind=all}</c>, end a placeholder too, after any
/// constraint (<c>{Id:int?}</c>, <c>{Page:int=1}</c>). Such a placeholder still binds its property; the route may leave
/// it out (<see cref="IsOptional"/>), so a URL built for a <see langword="null"/> property sends the default, or leaves an
/// optional placeholder out where that cannot move a later value into its place (<see cref="GetOmissible"/>).
/// </para>
/// <para>
/// A self link's or resource link's URL is registered with the suffix removed (<c>LinkGenerationModule</c>), so the router's
/// route value is keyed by the same name.
/// </para>
/// <para>
/// A placeholder finds its property ignoring case, and the route is registered under the property's own name: legacy read
/// the operation's property with <c>BindingFlags.IgnoreCase</c> and routed <c>"{" + property.Name + "}"</c>
/// (<c>ApiOperationLink.cs:59-61</c>, <c>:76</c>; Signature365 Analytics' <c>[RootLink("/track/{type}")]</c> on
/// <c>Type</c> was mapped as <c>api/track/{Type}</c>). <see cref="ResolveMemberName"/> and <see cref="Canonicalise"/> do
/// that once, where a module reads a template, so everything after compares ordinally, as it always has.
/// </para>
/// </remarks>
internal static class RoutePlaceholderSyntax
{
    /// <summary>
    /// Matches one placeholder; group 1 is the name of the operation property it binds, the <c>optional</c> group is a
    /// trailing <c>?</c>, and the <c>default</c> group is the value after a <c>=</c>.
    /// </summary>
    /// <remarks>
    /// After the name come legacy's <c>(format)</c>, then any number of <c>:constraint</c>s, each with an optional
    /// argument in parentheses. As ASP.NET's own parser does, an argument, like a format, runs to the first <c>)</c> that is
    /// followed by another constraint, the default or the end. So an <c>=</c> or <c>)</c> inside one is part of it
    /// (<c>{Code:regex(^a=b$)}</c>, <c>{Price(0.00;(0.00))}</c>), and a default may itself be in parentheses
    /// (<c>{Kind=(none)}</c>). A brace inside an argument or default is doubled, as ASP.NET escapes it.
    /// </remarks>
    public static Regex Placeholder { get; } = new(
        @"\{\*{0,2}([A-Za-z0-9_]+)(?:\((?:\}\}|[^}])*?\)(?=[:=]|\??\}))?(?::[^:=?(){}]*(?:\((?:\}\}|[^}])*?\)(?=[:=]|\??\}))?)*(?:(?<optional>\?)|=(?<default>(?:\}\}|[^}])*))?\}",
        RegexOptions.Compiled);

    /// <summary>
    /// Returns whether a <see cref="Placeholder"/> match is optional (<c>{Ext?}</c>) or has a default (<c>{Kind=all}</c>),
    /// so the route matches a URL without it.
    /// </summary>
    /// <param name="placeholder">A match of <see cref="Placeholder"/>.</param>
    /// <returns><see langword="true"/> when the route may leave the placeholder out.</returns>
    public static bool IsOptional(Match placeholder)
    {
        return placeholder.Groups["optional"].Success || placeholder.Groups["default"].Success;
    }

    /// <summary>
    /// Returns a <see cref="Placeholder"/> match's default value (<c>all</c> in <c>{Kind=all}</c>), with any doubled brace
    /// read as one, or <see langword="null"/> when it has none.
    /// </summary>
    /// <param name="placeholder">A match of <see cref="Placeholder"/>.</param>
    /// <returns>The default value, or <see langword="null"/>.</returns>
    public static string? GetDefault(Match placeholder)
    {
        var group = placeholder.Groups["default"];

        return group.Success ? group.Value.Replace("{{", "{").Replace("}}", "}") : null;
    }

    /// <summary>
    /// Returns the optional placeholders (<c>{Ext?}</c>) a URL built from <paramref name="template"/> can leave out: the
    /// run of them that ends the template, each with only the <c>/</c> or <c>.</c> before it, whose property can be
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// One followed by anything else cannot be left out: <c>/items/{Page?}/details</c> without <c>Page</c> would be
    /// <c>/items/details</c>, which the route does not match. Within the run, one that is a whole segment can be left out
    /// only when the next one is too, or a later value would move into its place: <c>/reports/{Year?}/{Month?}</c> without
    /// <c>Year</c> would be <c>/reports/12</c>, which binds <c>12</c> to <c>Year</c>. One after a <c>.</c> shares its segment
    /// with what comes before it, which keeps the segment there: <c>/files/{Name}.{Ext?}/{Revision?}</c> without <c>Ext</c>
    /// is <c>/files/abc/2</c> (<see cref="IsWholeSegment"/>).
    /// </remarks>
    /// <param name="template">The route template.</param>
    /// <param name="canBeNull">Whether the property a placeholder names can be <see langword="null"/>.</param>
    /// <returns>The run's matches, in template order.</returns>
    public static List<Match> GetTrailingOptional(string template, Func<string, bool> canBeNull)
    {
        var matches = Placeholder.Matches(template);
        var trailing = new List<Match>();
        var end = template.Length;

        for (var i = matches.Count - 1; i >= 0; i--)
        {
            var match = matches[i];

            if (match.Index + match.Length != end || !match.Groups["optional"].Success || !canBeNull(match.Groups[1].Value))
            {
                break;
            }

            trailing.Insert(0, match);
            end = match.Index > 0 && template[match.Index - 1] is '/' or '.' ? match.Index - 1 : match.Index;
        }

        return trailing;
    }

    /// <summary>
    /// Returns whether a placeholder of <paramref name="template"/> is a segment of its own, so leaving it out removes the
    /// segment, rather than following a <c>.</c> in a segment it shares.
    /// </summary>
    /// <param name="template">The route template.</param>
    /// <param name="placeholder">A match of <see cref="Placeholder"/> in <paramref name="template"/>.</param>
    /// <returns><see langword="true"/> when the placeholder starts its segment.</returns>
    public static bool IsWholeSegment(string template, Match placeholder)
    {
        return placeholder.Index == 0 || template[placeholder.Index - 1] == '/';
    }

    /// <summary>
    /// Returns the names of the placeholders of <paramref name="template"/> a URL built from it can go without a value
    /// for: a defaulted one, whose default it sends instead, and one of <see cref="GetTrailingOptional"/>, which it leaves
    /// out, when the property can be <see langword="null"/>.
    /// </summary>
    /// <param name="template">The route template.</param>
    /// <param name="canBeNull">Whether the property a placeholder names can be <see langword="null"/>.</param>
    /// <returns>The placeholder names, as written.</returns>
    public static HashSet<string> GetOmissible(string template, Func<string, bool> canBeNull)
    {
        var omissible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in Placeholder.Matches(template))
        {
            if (match.Groups["default"].Success && canBeNull(match.Groups[1].Value))
            {
                omissible.Add(match.Groups[1].Value);
            }
        }

        foreach (var match in GetTrailingOptional(template, canBeNull))
        {
            omissible.Add(match.Groups[1].Value);
        }

        return omissible;
    }

    /// <summary>
    /// Returns whether a property of <paramref name="type"/> can be <see langword="null"/>, so a URL built for it can go
    /// without its value: any reference type, or a <see cref="Nullable{T}"/>.
    /// </summary>
    /// <param name="type">The property's type.</param>
    /// <returns><see langword="true"/> when the property can be <see langword="null"/>.</returns>
    public static bool CanBeNull(ITypeSymbol type)
    {
        return !type.IsValueType || type.NullableAnnotation == NullableAnnotation.Annotated;
    }

    /// <summary>
    /// Finds the member a placeholder names: one spelled exactly as the placeholder, else a property that differs only in
    /// case (legacy's <c>GetProperty(..., IgnoreCase)</c>, which threw on two such properties, so the exact spelling is
    /// preferred).
    /// </summary>
    /// <param name="members">The members of the type the placeholder binds.</param>
    /// <param name="placeholderName">The name written in the placeholder.</param>
    /// <returns>The member's own name, or <see langword="null"/> when there is none.</returns>
    public static string? ResolveMemberName(IEnumerable<ISymbol> members, string placeholderName)
    {
        string? ignoringCase = null;

        foreach (var member in members)
        {
            if (string.Equals(member.Name, placeholderName, StringComparison.Ordinal))
            {
                return member.Name;
            }

            if (ignoringCase is null
                && member is IPropertySymbol
                && string.Equals(member.Name, placeholderName, StringComparison.OrdinalIgnoreCase))
            {
                ignoringCase = member.Name;
            }
        }

        return ignoringCase;
    }

    /// <summary>
    /// Rewrites each placeholder of <paramref name="template"/> to name the operation's property it binds (as
    /// <see cref="ResolveMemberName"/> finds it), leaving any <c>*</c>, <c>:ResourceProperty</c>, <c>(format)</c>, <c>?</c>
    /// or <c>=default</c> as written. A placeholder that names no property is left as it is, for the caller to report.
    /// </summary>
    /// <param name="template">The route template as declared.</param>
    /// <param name="operationType">The operation whose properties the placeholders bind.</param>
    /// <returns>The template with each placeholder spelled as its property.</returns>
    public static string Canonicalise(string template, ITypeSymbol operationType)
    {
        if (!template.Contains('{'))
        {
            return template;
        }

        var members = operationType is INamedTypeSymbol namedOperationType
            ? Planning.OperationPropertyModelBuilder.GetMembersWithContributed(namedOperationType).ToList()
            : operationType.GetAllMembers().ToList();

        return Placeholder.Replace(
            template,
            match =>
            {
                var name = match.Groups[1];
                var resolved = ResolveMemberName(members, name.Value);

                if (resolved is null || resolved == name.Value)
                {
                    return match.Value;
                }

                var offset = name.Index - match.Index;

                return string.Concat(match.Value.Substring(0, offset), resolved, match.Value.Substring(offset + name.Length));
            });
    }
}

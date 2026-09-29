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
    /// Matches one placeholder; group 1 is the name of the operation property it binds.
    /// </summary>
    public static Regex Placeholder { get; } = new(@"\{\*{0,2}([A-Za-z0-9_]+)(?:[:(][^}]*)?\}", RegexOptions.Compiled);

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
    /// <see cref="ResolveMemberName"/> finds it), leaving any <c>*</c>, <c>:ResourceProperty</c> or <c>(format)</c> as
    /// written. A placeholder that names no property is left as it is, for the caller to report.
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

        var members = operationType.GetAllMembers().ToList();

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

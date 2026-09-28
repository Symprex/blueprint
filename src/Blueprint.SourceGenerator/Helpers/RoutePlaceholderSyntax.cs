using System.Text.RegularExpressions;

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
/// </remarks>
internal static class RoutePlaceholderSyntax
{
    /// <summary>
    /// Matches one placeholder; group 1 is the name of the operation property it binds.
    /// </summary>
    public static Regex Placeholder { get; } = new(@"\{\*{0,2}([A-Za-z0-9_]+)(?:[:(][^}]*)?\}", RegexOptions.Compiled);
}

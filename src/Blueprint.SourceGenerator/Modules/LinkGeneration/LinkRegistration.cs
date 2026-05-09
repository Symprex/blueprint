using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.LinkGeneration;

/// <summary>
/// Records a single placeholder mapping within a link URL: from the route parameter name
/// to the resource type property that supplies the value at runtime.
/// </summary>
/// <param name="RouteName">The placeholder name in the URL (e.g., <c>Id</c> from <c>{Id}</c>).</param>
/// <param name="ResourcePropertyName">
/// The property on the resource type to read (may differ when the self-link uses
/// <c>{RouteName:ResourceProp}</c> mapped syntax).
/// </param>
public sealed record LinkPlaceholderMapping(string RouteName, string ResourcePropertyName);

/// <summary>
/// Records a self-link registration discovered from a <c>[SelfLink]</c> attribute on an operation.
/// This metadata is added to the descriptor plan and can be read by manifest modules.
/// </summary>
/// <param name="Url">The URL template (e.g., <c>/items/{Id}</c>).</param>
/// <param name="Rel">The link relation (typically <c>"self"</c>).</param>
/// <param name="ResourceType">The resource type that this self-link targets.</param>
/// <param name="LocationInfo">The source location of the attribute argument for diagnostics.</param>
/// <param name="PlaceholderMappings">
/// The resolved placeholder-to-resource-property mappings for compiled URL building.
/// Empty when the URL has no placeholders.
/// </param>
public sealed record LinkRegistration(
    string Url,
    string Rel,
    INamedTypeSymbol ResourceType,
    LocationInfo LocationInfo,
    EquatableList<LinkPlaceholderMapping> PlaceholderMappings);

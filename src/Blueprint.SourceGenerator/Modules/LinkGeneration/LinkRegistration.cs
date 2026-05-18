using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.LinkGeneration;

/// <summary>
/// Records a single placeholder mapping within a link URL: from the route parameter name
/// to the resource type property (or properties) that supply the value at runtime.
/// Supports fallback semantics: when <c>FallbackPropertyName</c> is set, the generated code
/// tries <c>ResourcePropertyName</c> first, then falls back to the alternative.
/// </summary>
/// <param name="RouteName">The placeholder name in the URL (e.g., <c>Id</c> from <c>{Id}</c>).</param>
/// <param name="ResourcePropertyName">
/// The primary property on the resource type to read. When only this exists on the resource,
/// it is used directly. When both primary and fallback exist, primary is tried first.
/// </param>
/// <param name="IsValueType">
/// Whether the primary resource property type is a non-nullable value type.
/// </param>
/// <param name="FallbackPropertyName">
/// Optional fallback property on the resource. Used when the primary is null or absent.
/// Null when no fallback was specified (simple <c>{Prop}</c> syntax).
/// </param>
/// <param name="IsFallbackValueType">
/// Whether the fallback property type is a non-nullable value type. Only relevant
/// when <see cref="FallbackPropertyName"/> is not null.
/// </param>
public sealed record LinkPlaceholderMapping(
    string RouteName,
    string ResourcePropertyName,
    bool IsValueType,
    string? FallbackPropertyName = null,
    bool IsFallbackValueType = false);

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

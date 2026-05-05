using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.LinkGeneration;

/// <summary>
/// Records a self-link registration discovered from a <c>[SelfLink]</c> attribute on an operation.
/// This metadata is added to the descriptor plan and can be read by manifest modules.
/// </summary>
/// <param name="Url">The URL template (e.g., <c>/items/{Id}</c>).</param>
/// <param name="Rel">The link relation (typically <c>"self"</c>).</param>
/// <param name="ResourceType">The resource type that this self-link targets.</param>
/// <param name="LocationInfo">The source location of the attribute argument for diagnostics.</param>
public sealed record LinkRegistration(string Url, string Rel, INamedTypeSymbol ResourceType, LocationInfo LocationInfo);

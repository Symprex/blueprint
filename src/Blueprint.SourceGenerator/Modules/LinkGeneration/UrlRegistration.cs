using Blueprint.SourceGenerator.Helpers;

namespace Blueprint.SourceGenerator.Modules.LinkGeneration;

/// <summary>
/// Records a root URL registration discovered from a <c>[RootLink]</c> attribute on an operation.
/// This metadata is added to the descriptor plan and can be read by manifest modules.
/// </summary>
/// <param name="Url">The URL template (e.g., <c>/items/{Id}</c>).</param>
/// <param name="LocationInfo">The source location of the attribute argument for diagnostics.</param>
public sealed record UrlRegistration(string Url, LocationInfo? LocationInfo);

using Blueprint.SourceGenerator.Planning;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Modules.LinkGeneration;

/// <summary>
/// Contributes <c>links.urls</c> and <c>links.selfLinks</c> to the per-operation debug manifest,
/// reflecting the URL templates and self-link declarations discovered by <see cref="LinkGenerationModule"/>.
/// </summary>
/// <remarks>
/// This module reads <see cref="UrlRegistration"/> and <see cref="LinkRegistration"/> metadata from
/// the descriptor plan that <see cref="LinkGenerationModule"/> projects during its
/// <c>ContributeToDescriptor</c> phase. It must therefore run in the same configuration as
/// <see cref="LinkGenerationModule"/>.
/// </remarks>
internal sealed class LinkManifestModule : IBlueprintSourceGeneratorModule, IDebugManifestContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(LinkManifestModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(LinkManifestModule);

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn => [];

    /// <inheritdoc />
    public void ContributeToManifest(DebugManifestContributorContext context)
    {
        var urls = context.GetMetadata<UrlRegistration>().ToList();
        var links = context.GetMetadata<LinkRegistration>().ToList();

        if (urls.Count > 0)
        {
            var urlArray = new JArray(urls.Select(static u => new JValue(u.Url)));
            context.AddEntry("links.urls", urlArray);
        }

        if (links.Count > 0)
        {
            var selfLinksArray = new JArray(links.Select(static l => new JObject
            {
                ["url"] = l.Url,
                ["rel"] = l.Rel,
                ["resourceType"] = l.ResourceType.Name,
            }));

            context.AddEntry("links.selfLinks", selfLinksArray);
        }
    }
}

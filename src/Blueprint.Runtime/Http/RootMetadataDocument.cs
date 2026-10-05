using System.Collections.Immutable;
using Blueprint.Model;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Builds the root-metadata document an app answers <c>GET</c> at its base path with, as legacy Blueprint's
/// <c>RootMetadataOperation</c> did (Signature365 <c>8b40ecc961</c>, <c>Blueprint.Http/RootMetadataOperation.cs</c>): an
/// empty <see cref="RootResource"/> whose <c>$links</c> hold one link per root link of every exposed operation.
/// </summary>
/// <remarks>
/// <para>
/// Legacy registered the operation in every HTTP app (<c>BlueprintApiBuilderExtensions.cs:84-92</c>) as
/// <c>[RootLink("/", Rel = "self")]</c>, <c>[AllowAnonymous]</c>, <c>[DoNotAudit]</c> and <c>[UnexposedOperation]</c>
/// (<c>RootMetadataOperation.cs:10-14</c>), so it needed no user, was never audited, and neither listed itself nor
/// appeared in the OpenAPI document. The HTTP host serves it outside any operation pipeline, which gives exactly that.
/// </para>
/// <para>
/// Its links were the root links — those with no resource type (<c>ApiDataModel.cs:212-215</c>) — of the exposed operations
/// (<c>RootMetadataOperation.cs:27-29</c>), keyed by rel, with the request's base URI followed by the link's URL as the href,
/// its placeholders left as a template for the client (<c>:31-38</c>; <c>ApiLinkGenerator.cs:92-118</c>). Legacy's template
/// was the link's routing URL, which only differed from its URL by a placeholder's casing, alternate name or format and by
/// a query string (<c>ApiOperationLink.cs:29-30</c>, <c>:48-78</c>); this framework requires each placeholder to name a
/// member exactly (<c>BPLG001</c>) and routes a root link's URL as declared, so none of those can occur and the two are the
/// same.
/// </para>
/// <para>
/// Nothing else added links: legacy apps without link generation served the same document
/// (EventStore's generated <c>RootMetadataOperationExecutorPipeline</c> has no link step), and the resource links of
/// <c>RootResource</c>, a type no operation links, were none.
/// </para>
/// </remarks>
internal static class RootMetadataDocument
{
    /// <summary>
    /// Collects, once, the operations of <paramref name="app"/> that declare root links, with those links.
    /// </summary>
    /// <param name="app">The app being mapped.</param>
    /// <returns>Each operation that has root links, with them, in registration order.</returns>
    internal static ImmutableArray<(ApiOperationDescriptor Operation, ImmutableArray<OperationRootLink> Links)> CollectRootLinks(BlueprintApp app)
    {
        return
        [
            .. app.Operations
                .Select(static operation => (
                    Operation: operation,
                    Links: operation.TryGetFeatureData<OperationRootLinksFeatureData>(out var data) ? data.Links : ImmutableArray<OperationRootLink>.Empty))
                .Where(static entry => entry.Links.Length > 0),
        ];
    }

    /// <summary>
    /// Builds the document for one request, whose base URI the hrefs are made under.
    /// </summary>
    /// <param name="baseUri">The request's base URI, as every link of the request is made under.</param>
    /// <param name="rootLinks">The app's root links, from <see cref="CollectRootLinks"/>.</param>
    /// <returns>The document to write.</returns>
    /// <exception cref="InvalidOperationException">
    /// When two root links share a rel but not an href, as legacy's <c>ApiResource.AddLink</c> threw.
    /// </exception>
    internal static RootResource Create(
        string baseUri,
        ImmutableArray<(ApiOperationDescriptor Operation, ImmutableArray<OperationRootLink> Links)> rootLinks)
    {
        var prefix = baseUri.EndsWith('/') ? baseUri : baseUri + "/";
        var document = new RootResource();

        foreach (var (operation, links) in rootLinks)
        {
            // Read per request, as legacy read it: a descriptor can be hidden after the app was mapped.
            if (!operation.IsExposed)
            {
                continue;
            }

            foreach (var link in links)
            {
                document.AddLink(link.Rel, new Link { Href = prefix + link.UrlFormat.TrimStart('/') });
            }
        }

        return document;
    }
}

/// <summary>
/// The root-metadata document's resource, as legacy's <c>Blueprint.Http.RootResource</c> was: an <see cref="ApiResource"/>
/// with no properties of its own, so it is written as <c>$object</c> <c>"root"</c>, its <c>$links</c> and <c>$partial</c>.
/// </summary>
internal sealed class RootResource : ApiResource
{
}

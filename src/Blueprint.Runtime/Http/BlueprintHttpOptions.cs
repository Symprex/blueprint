using Microsoft.AspNetCore.Mvc;

namespace Blueprint.Runtime.Http;

/// <summary>
/// A set of options that are used throughout the Blueprint HTTP support module.
/// </summary>
public sealed class BlueprintHttpOptions
{
    /// <summary>
    /// Whether to expose exception details when formatting unhandled exceptions in the pipeline. If this
    /// is <c>true</c> we will populate a formatted <see cref="ProblemDetails" /> instance with the exception message
    /// and stack trace, otherwise a generic message will be used and no stack trace will be present.
    /// </summary>
    public bool ExposeExceptionDetailsInErrorResponses { get; set; }

    /// <summary>
    /// Gets or sets the optional embedded resource name for the canonical operations manifest that hosted inspection
    /// surfaces should expose. Keeping this explicit avoids introducing a hidden runtime resource-discovery convention
    /// before the manifest emission story is widened deliberately.
    /// </summary>
    public string? OperationsManifestResourceName { get; set; }

    /// <summary>
    /// Gets or sets the optional embedded resource name for a prebuilt markdown projection of the operations manifest.
    /// Keeping markdown bytes explicit lets hosted inspection widen without moving markdown projection logic into the
    /// runtime assembly.
    /// </summary>
    public string? OperationsManifestMarkdownResourceName { get; set; }

    /// <summary>
    /// Gets or sets the optional embedded resource name for the checked-in schema that describes the hosted operations
    /// manifest contract. Keeping schema bytes explicit lets runtime inspection widen without moving schema lookup or
    /// schema interpretation logic into the runtime assembly.
    /// </summary>
    public string? OperationsManifestSchemaResourceName { get; set; }

    /// <summary>
    /// An optional domain that should be used instead of the request URL when generating absolute
    /// link URLs.
    /// </summary>
    /// <remarks>
    /// By default this is <c>null</c> which means when generating URLs Blueprint will look at the
    /// incoming HTTP request and use the <see cref="HttpRequest.Host" /> property.
    /// </remarks>
    public string? PublicHost { get; set; }

    /// <summary>
    /// An optional authority, which may carry a path prefix as well as a host (<c>gateway.example.com/import</c>), that
    /// this API should advertise to its clients as its own address — the default OpenAPI <c>servers</c> entry, with the
    /// base path the API is mapped at appended.
    /// </summary>
    /// <remarks>
    /// <para>
    /// By default this is <c>null</c>, which omits the <c>servers</c> array entirely (unless
    /// <see cref="OpenApiInfoOptions.Servers" /> is configured explicitly, which always wins).
    /// <see cref="PublicHost" /> is not consulted as a fallback, and neither is the incoming request, since the
    /// OpenAPI document is rendered once at registration, before any request exists.
    /// </para>
    /// <para>
    /// Set this where the address clients should call differs from the one links are built against — for example a
    /// gateway that fronts this API on a different host or strips a path prefix before the request reaches it. Give
    /// the gateway's authority and prefix only, not the base path passed to <c>MapBlueprintApi</c>: that is appended,
    /// as legacy Blueprint appended it, so <c>gateway.example.com/import</c> for an API mapped at <c>api/</c> is
    /// advertised as <c>https://gateway.example.com/import/api/</c>. The document's paths are relative to that
    /// address and do not carry the base path.
    /// </para>
    /// </remarks>
    public string? PublicApiHost { get; set; }
}

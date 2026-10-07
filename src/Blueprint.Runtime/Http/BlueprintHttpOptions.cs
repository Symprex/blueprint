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
    /// Whether an error response's <c>Content-Type</c> is chosen from the request's <c>Accept</c> header as legacy
    /// Blueprint chose it, rather than always being <c>application/problem+json; charset=utf-8</c>. <see langword="false"/>
    /// by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Legacy Blueprint wrote every error — a validation failure, an authorisation denial, an <see cref="ApiException"/>
    /// or any other exception — through the same output formatter as a success, and chose its content type by content
    /// negotiation. With no <c>Accept</c> header, or one that includes <c>*/*</c> anywhere, it wrote exactly
    /// <c>application/json</c>, with no charset. Otherwise it took the accepted media types in descending quality. The
    /// first that is <c>application/json</c>, <c>text/json</c> or an <c>application/*+json</c> type (including
    /// <c>application/problem+json</c>) gave the content type. Its content type was the whole <c>Accept</c> header line
    /// that media type came from. With none of them, it wrote <c>application/json</c>.
    /// </para>
    /// <para>
    /// Turn this on for clients that read a body as JSON only when its content type contains <c>application/json</c>
    /// and send no <c>Accept</c>, as legacy's own clients did. Pair it with
    /// <see cref="OpenApiInfoOptions.UsePlainJsonForErrorResponses"/> so that the OpenAPI document describes what the
    /// wire carries.
    /// </para>
    /// </remarks>
    public bool NegotiateErrorContentType { get; set; }

    /// <summary>
    /// Whether the OpenAPI document is rendered when it is first requested or read, rather than as the API is mapped.
    /// <see langword="false"/> by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// By default the document is rendered by <c>MapBlueprintApi</c>, so a document that cannot be rendered fails the
    /// host's startup, and every request is served the rendered document.
    /// </para>
    /// <para>
    /// Rendering is most of what mapping a large API costs, and a process that builds many hosts pays it for every one,
    /// whether or not anything reads the document. Turn this on there - a test suite that builds a host per test is the
    /// case it exists for. The document is then rendered once, by the first request for it or the first read of
    /// <see cref="PreRenderedOpenApiDocumentHolder.Document" /> (which <c>OutputOpenApi</c> makes), and a document that
    /// cannot be rendered fails that request instead of the startup.
    /// </para>
    /// </remarks>
    public bool RenderOpenApiDocumentOnFirstRequest { get; set; }

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

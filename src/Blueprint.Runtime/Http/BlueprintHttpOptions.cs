using Microsoft.AspNetCore.Mvc;

namespace Blueprint.Runtime.Http;

/// <summary>
/// A set of options that are used throughout the Blueprint HTTP support module.
/// </summary>
public class BlueprintHttpOptions
{
    /// <summary>
    /// Whether to expose exception details when formatting unhandled exceptions in the pipeline. If this
    /// is <c>true</c> we will populate a formatted <see cref="ProblemDetails" /> instance with the exception message
    /// and stack trace, otherwise a generic message will be used and no stack trace will be present.
    /// </summary>
    public bool ExposeExceptionDetailsInErrorResponses { get; set; }

    /// <summary>
    /// Gets or sets the flag which causes content negotiation to ignore Accept header
    /// when it contains the media type */*. <see langword="false"/> by default.
    /// </summary>
    public bool RespectBrowserAcceptHeader { get; set; }

    /// <summary>
    /// Gets or sets the flag which decides whether an HTTP 406 Not Acceptable response
    /// will be returned if no formatter has been selected to format the response.
    /// <see langword="false"/> by default.
    /// </summary>
    public bool ReturnHttpNotAcceptable { get; set; }

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
    /// An optional domain that should be used instead of the request URL when generating absolute
    /// link URLs.
    /// </summary>
    /// <remarks>
    /// By default this is <c>null</c> which means when generating URLs Blueprint will look at the
    /// incoming HTTP request and use the <see cref="HttpRequest.Host" /> property.
    /// </remarks>
    public string? PublicHost { get; set; }
}

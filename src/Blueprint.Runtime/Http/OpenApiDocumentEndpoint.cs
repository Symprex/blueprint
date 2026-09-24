using System.Security.Cryptography;
using System.Text;
using Blueprint.Model;
using Microsoft.Net.Http.Headers;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Builds the always-on OpenAPI document endpoint that serves the rendered OpenAPI 3.x JSON for the Blueprint app.
/// OpenAPI is a required part of the HTTP host and is therefore wired unconditionally, not just in development.
/// </summary>
internal static class OpenApiDocumentEndpoint
{
    /// <summary>
    /// Creates the request delegate that renders and serves the OpenAPI 3.x JSON document for the given Blueprint app.
    /// The document is assembled once at registration time from the app's generated descriptor and serializer metadata,
    /// so each request receives a pre-rendered response without repeating the rendering chain per call.
    /// </summary>
    /// <param name="app">The Blueprint app whose registered operations should be projected into the OpenAPI document.</param>
    /// <param name="infoOptions">
    /// Optional customisation for the OpenAPI <c>info</c> block (title, version, description).
    /// When <see langword="null"/> the defaults from <see cref="OpenApiInfoOptions"/> are used.
    /// </param>
    /// <param name="schemaProcessorOptions">
    /// Optional ordered schema processors that can customise rendered schema components before the document is cached.
    /// When <see langword="null"/> no schema processors are applied.
    /// </param>
    /// <param name="operationProcessorOptions">
    /// Optional ordered per-operation processors that can customise rendered operation objects before the document is cached.
    /// When <see langword="null"/> no operation processors are applied.
    /// </param>
    /// <param name="httpOptions">
    /// Optional Blueprint HTTP options consulted when no explicit <see cref="OpenApiInfoOptions.Servers"/> are
    /// configured, to populate a default <c>servers</c> entry from <see cref="BlueprintHttpOptions.PublicApiHost"/>
    /// followed by <paramref name="basePath"/>. When <see langword="null"/> the <c>servers</c> array stays
    /// omitted unless <see cref="OpenApiInfoOptions.Servers"/> already carries entries.
    /// </param>
    /// <param name="basePath">
    /// The base path the API's routes are mapped under, which the default <c>servers</c> entry ends with; the document's
    /// <c>paths</c> are relative to that entry and do not carry it. <see langword="null"/> when unknown.
    /// </param>
    /// <returns>A request delegate that writes the rendered OpenAPI 3.x JSON to the response.</returns>
    public static (RequestDelegate Delegate, PreRenderedOpenApiDocument Document) Create(
        BlueprintApp app,
        OpenApiInfoOptions? infoOptions = null,
        OpenApiSchemaProcessorOptions? schemaProcessorOptions = null,
        OpenApiOperationProcessorOptions? operationProcessorOptions = null,
        BlueprintHttpOptions? httpOptions = null,
        string? basePath = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        var (json, warnings) = RenderDocument(app, infoOptions, schemaProcessorOptions, operationProcessorOptions, httpOptions, basePath);
        var document = new PreRenderedOpenApiDocument(json) { Warnings = warnings };

        // Compute a stable ETag from the document content so clients can use If-None-Match
        // to avoid redundant downloads of the static spec.
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var hashBytes = SHA256.HashData(jsonBytes);
        var etag = new EntityTagHeaderValue($"\"{Convert.ToHexString(hashBytes)[..16]}\"");

        var handler = new RequestDelegate(async httpContext =>
        {
            // Support conditional GET: if the client sends If-None-Match matching our ETag, return 304.
            if (httpContext.Request.Headers.IfNoneMatch.Count > 0)
            {
                foreach (var clientEtag in httpContext.Request.Headers.IfNoneMatch)
                {
                    if (string.Equals(clientEtag, etag.ToString(), StringComparison.Ordinal))
                    {
                        httpContext.Response.StatusCode = StatusCodes.Status304NotModified;
                        httpContext.Response.Headers.ETag = etag.ToString();
                        return;
                    }
                }
            }

            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "application/json; charset=utf-8";
            httpContext.Response.Headers.ETag = etag.ToString();
            httpContext.Response.Headers.CacheControl = "public, max-age=3600";
            await httpContext.Response.WriteAsync(json, httpContext.RequestAborted).ConfigureAwait(false);
        });

        return (handler, document);
    }

    private static (string Json, IReadOnlyList<string> Warnings) RenderDocument(
        BlueprintApp app,
        OpenApiInfoOptions? infoOptions,
        OpenApiSchemaProcessorOptions? schemaProcessorOptions,
        OpenApiOperationProcessorOptions? operationProcessorOptions,
        BlueprintHttpOptions? httpOptions,
        string? basePath)
    {
        var documentBuilderContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaProjectionContext = OpenApiSchemaProjectionContext.Create(
            documentBuilderContext,
            infoOptions?.SchemaNaming ?? OpenApiSchemaNaming.Default,
            infoOptions?.RequiredProperties ?? OpenApiRequiredProperties.Declared);
        var responseSchemaProjectionContext = OpenApiResponseSchemaReferenceProjectionContext.Create(schemaProjectionContext);
        var responseObjectContext = OpenApiResponseObjectRenderingContext.Create(
            responseSchemaProjectionContext,
            infoOptions?.UsePlainJsonForErrorResponses ?? false);
        var operationContext = OpenApiOperationRenderingContext.Create(
            responseObjectContext,
            infoOptions?.RequestBodyNaming ?? OpenApiRequestBodyNaming.OperationName,
            infoOptions?.QueryParameterNaming ?? OpenApiParameterNaming.BindingName);

        // Apply per-operation processors before path-item grouping so consumers can strip
        // auto-populated parameters, override summaries, etc.
        if (operationProcessorOptions?.Processors is { Count: > 0 })
        {
            operationContext = operationContext.ApplyProcessors(app, operationProcessorOptions);
        }

        // When the consumer opts in to legacy compatibility, add synthetic 200 aliases for
        // body-bearing non-200 success responses so TypeScript generators that hardcode
        // responses['200'] still produce the correct return types.
        if (infoOptions?.UseStatus200ForSuccessResponses == true)
        {
            operationContext = operationContext.NormalizeSuccessResponsesTo200();
        }

        var pathItemContext = OpenApiPathItemRenderingContext.Create(operationContext);
        var documentContext = OpenApiDocumentRenderingContext.Create(pathItemContext);

        return (OpenApiOutputRenderingContext.Create(documentContext, infoOptions, schemaProcessorOptions, httpOptions, basePath).Json, operationContext.Warnings);
    }
}

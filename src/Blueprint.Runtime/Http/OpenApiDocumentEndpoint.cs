using Blueprint.Model;

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
    /// <returns>A request delegate that writes the rendered OpenAPI 3.x JSON to the response.</returns>
    public static RequestDelegate Create(BlueprintApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var json = RenderDocument(app);

        return async httpContext =>
        {
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "application/json; charset=utf-8";
            await httpContext.Response.WriteAsync(json, httpContext.RequestAborted);
        };
    }

    private static string RenderDocument(BlueprintApp app)
    {
        var documentBuilderContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaProjectionContext = OpenApiSchemaProjectionContext.Create(documentBuilderContext);
        var responseSchemaProjectionContext = OpenApiResponseSchemaReferenceProjectionContext.Create(schemaProjectionContext);
        var responseObjectContext = OpenApiResponseObjectRenderingContext.Create(responseSchemaProjectionContext);
        var operationContext = OpenApiOperationRenderingContext.Create(responseObjectContext);
        var pathItemContext = OpenApiPathItemRenderingContext.Create(operationContext);
        var documentContext = OpenApiDocumentRenderingContext.Create(pathItemContext);

        return OpenApiOutputRenderingContext.Create(documentContext).Json;
    }
}

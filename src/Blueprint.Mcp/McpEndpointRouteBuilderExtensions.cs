using Blueprint.Mcp;
using Blueprint.Model;
using Blueprint.Runtime.Http;
using Microsoft.Extensions.Options;

// This is the recommendation from MS for extensions to IEndpointRouteBuilder to aid discoverability
// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Maps a Blueprint app's operations as MCP tools.
/// </summary>
public static class McpEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Serves one MCP tool per operation <typeparamref name="TApp"/> exposes over HTTP, at <paramref name="path"/>, over
    /// the MCP SDK's Streamable HTTP transport (D22).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A tool is named by its operation's OpenAPI <c>operationId</c> and described by the operation's input schema
    /// (<see cref="IOperationInputSchemaProvider"/>), its summary and its description. Calling it makes the HTTP request
    /// the URL and method its <c>operationId</c> names answer, from the tool's arguments, and runs it in process through
    /// that endpoint: the same ASP.NET Core authorisation policies, binding, validation, authentication and authorisation
    /// as HTTP, as the MCP request's caller, with its <c>Authorization</c> header. The result is the response the HTTP
    /// caller would have been given: its body as the tool's text (and as its structured content when the body is a JSON
    /// object), its status as <c>_meta.httpStatus</c>, and a 4xx or 5xx status as a tool error.
    /// </para>
    /// <para>
    /// Map it after <c>MapBlueprintApi</c> for the same app, whose input schemas it reads, and register it with
    /// <c>AddBlueprintMcp</c>. An operation whose input schema cannot be made fails this call, as it fails the OpenAPI
    /// document, unless <see cref="BlueprintHttpOptions.RenderOpenApiDocumentOnFirstRequest"/> defers both to their first
    /// use. Listing tools needs no authentication, as reading the OpenAPI document needs none; add
    /// <c>RequireAuthorization</c> to the returned builder to require it.
    /// </para>
    /// </remarks>
    /// <typeparam name="TApp">The Blueprint app whose operations become tools.</typeparam>
    /// <param name="endpoints">The endpoint route builder the app's API is mapped on.</param>
    /// <param name="path">The route the MCP endpoint answers at, such as <c>mcp</c> or <c>api/mcp</c>.</param>
    /// <returns>A builder for conventions on the MCP endpoint.</returns>
    /// <exception cref="InvalidOperationException">
    /// <c>AddBlueprintMcp</c> was not called, <c>MapBlueprintApi</c> has not mapped <typeparamref name="TApp"/>, or an
    /// operation's input schema cannot be made.
    /// </exception>
    public static IEndpointConventionBuilder MapBlueprintMcp<TApp>(this IEndpointRouteBuilder endpoints, string path)
        where TApp : BlueprintApp
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(path);

        var services = endpoints.ServiceProvider;

        if (services.GetService<BlueprintMcpMarker>() is null)
        {
            throw new InvalidOperationException(
                $"MapBlueprintMcp<{typeof(TApp).Name}> needs the services AddBlueprintMcp registers. Call services.AddBlueprintMcp().");
        }

        var app = services.GetService<TApp>()
                  ?? throw new InvalidOperationException(
                      $"{typeof(TApp).Name} is not registered. Call AddBlueprintApi for it, and MapBlueprintApi before MapBlueprintMcp.");

        var httpOptions = services.GetService<IOptions<BlueprintHttpOptions>>()?.Value ?? new BlueprintHttpOptions();

        // The registered provider holds the schemas of the app MapBlueprintApi mapped last. Keep this app's, which a tool
        // set made on first request (RenderOpenApiDocumentOnFirstRequest) would otherwise read after another's replaced them.
        var schemaProvider = services.GetRequiredService<IOperationInputSchemaProvider>();

        if (schemaProvider is OperationInputSchemaProvider mappedSchemas)
        {
            schemaProvider = mappedSchemas.Snapshot();
        }

        var toolSet = new BlueprintMcpToolSet(app, schemaProvider, endpoints.DataSources);

        if (!httpOptions.RenderOpenApiDocumentOnFirstRequest)
        {
            toolSet.EnsureToolsCreated();
        }

        var builder = endpoints.MapMcp(path);
        builder.WithMetadata(toolSet);

        return builder;
    }
}

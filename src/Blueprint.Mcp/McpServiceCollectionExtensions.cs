using Blueprint.Mcp;
using ModelContextProtocol.AspNetCore;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the services <c>MapBlueprintMcp</c> needs.
/// </summary>
public static class McpServiceCollectionExtensions
{
    /// <summary>
    /// Registers the MCP server, over the official C# MCP SDK's Streamable HTTP transport, that <c>MapBlueprintMcp</c>
    /// serves a Blueprint app's operations through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The server is stateless: every MCP request is answered within its own HTTP request, so a tool call runs as the
    /// caller that request authenticated, with that request's bearer token, exactly as the operation's HTTP endpoint
    /// would. A stateful session would answer later calls with the identity of the request that opened it.
    /// </para>
    /// <para>
    /// Blueprint owns the app's MCP server: each endpoint <c>MapBlueprintMcp</c> maps is given its app's tools per
    /// request, so several apps can be served at different paths of one host.
    /// </para>
    /// </remarks>
    /// <param name="services">The application's services.</param>
    /// <returns><paramref name="services"/>.</returns>
    public static IServiceCollection AddBlueprintMcp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(static d => d.ServiceType == typeof(BlueprintMcpMarker)))
        {
            return services;
        }

        services.AddSingleton<BlueprintMcpMarker>();
        services.AddHttpContextAccessor();

        services
            .AddMcpServer()
            .WithHttpTransport(static transport =>
            {
                transport.SessionMode = HttpServerSessionMode.Stateless;
                transport.ConfigureSessionOptions = static (httpContext, options, _) =>
                {
                    // In stateless mode this runs for every request, so the endpoint it matched says whose tools to serve.
                    if (httpContext.GetEndpoint()?.Metadata.GetMetadata<BlueprintMcpToolSet>() is { } toolSet)
                    {
                        options.ToolCollection = toolSet.CreateToolCollection();
                    }

                    return Task.CompletedTask;
                };
            });

        return services;
    }
}

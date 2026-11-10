using Blueprint.Model;
using Blueprint.Runtime.Http;
using ModelContextProtocol.Server;

namespace Blueprint.Mcp;

/// <summary>
/// The tools one <c>MapBlueprintMcp</c> endpoint serves, one per operation its app exposes over HTTP. It is the MCP
/// endpoint's metadata, which <c>AddBlueprintMcp</c>'s per-request session callback reads to give the request its tools.
/// </summary>
/// <remarks>
/// The tools are made once, from the input schemas, which agree with the OpenAPI document because they come from the
/// same rendering. Each tool finds the API endpoint it runs through on its first call, when every endpoint convention
/// has been applied.
/// </remarks>
internal sealed class BlueprintMcpToolSet
{
    private readonly BlueprintApp _app;
    private readonly IOperationInputSchemaProvider _schemaProvider;
    private readonly ICollection<EndpointDataSource> _dataSources;
    private readonly Lazy<IReadOnlyList<BlueprintOperationTool>> _tools;

    /// <summary>
    /// Initializes a new instance of the <see cref="BlueprintMcpToolSet"/> class.
    /// </summary>
    /// <param name="app">The app whose operations become tools.</param>
    /// <param name="schemaProvider">The input schemas <c>MapBlueprintApi</c> made for the app.</param>
    /// <param name="dataSources">The endpoint data sources the app's API is mapped into.</param>
    public BlueprintMcpToolSet(BlueprintApp app, IOperationInputSchemaProvider schemaProvider, ICollection<EndpointDataSource> dataSources)
    {
        this._app = app;
        this._schemaProvider = schemaProvider;
        this._dataSources = dataSources;
        this._tools = new Lazy<IReadOnlyList<BlueprintOperationTool>>(this.CreateTools, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Makes the tools now, so an operation whose schema cannot be made fails the caller instead of the first MCP request.
    /// </summary>
    public void EnsureToolsCreated()
    {
        _ = this._tools.Value;
    }

    /// <summary>
    /// Gives a new collection of this endpoint's tools, for one MCP request. The tools themselves are shared.
    /// </summary>
    /// <returns>A collection the MCP server can own for the request.</returns>
    public McpServerPrimitiveCollection<McpServerTool> CreateToolCollection()
    {
        var collection = new McpServerPrimitiveCollection<McpServerTool>();

        foreach (var tool in this._tools.Value)
        {
            collection.Add(tool);
        }

        return collection;
    }

    /// <summary>
    /// Finds the endpoint <c>MapBlueprintApi</c> mapped for <paramref name="operation"/> at the URL
    /// <paramref name="schema"/> describes, or at the first URL it mapped for the operation, as it maps an operation's
    /// URLs in the order they are declared, when the schema names none.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <param name="schema">The operation's input schema.</param>
    /// <returns>The endpoint, or <see langword="null"/> when none is mapped.</returns>
    internal RouteEndpoint? FindEndpoint(ApiOperationDescriptor operation, OperationInputSchema schema)
    {
        RouteEndpoint? best = null;
        var bestRank = int.MaxValue;

        foreach (var dataSource in this._dataSources)
        {
            foreach (var endpoint in dataSource.Endpoints)
            {
                if (endpoint is not RouteEndpoint routeEndpoint
                    || endpoint.Metadata.GetMetadata<ApiOperationDescriptor>() is not { } mapped
                    || (!ReferenceEquals(mapped, operation) && mapped.OperationType != operation.OperationType))
                {
                    continue;
                }

                // The app's own descriptor before another's of the same type, as two apps mapped on one host may share
                // an operation type, and the schema's URL before the first one mapped.
                var isAtSchemaUrl = schema.UrlFormat is null
                                    || string.Equals(endpoint.Metadata.GetMetadata<ApiOperationUrl>()?.UrlFormat, schema.UrlFormat, StringComparison.Ordinal);
                var rank = (ReferenceEquals(mapped, operation) ? 0 : 2) + (isAtSchemaUrl ? 0 : 1);

                if (rank < bestRank)
                {
                    best = routeEndpoint;
                    bestRank = rank;
                }
            }
        }

        return best;
    }

    private IReadOnlyList<BlueprintOperationTool> CreateTools()
    {
        var operations = this._app.Operations
            .Where(static o => o.Urls.Count > 0)
            .ToDictionary(static o => o.Name, StringComparer.Ordinal);

        var tools = this._schemaProvider.GetSchemas()
            .Where(schema => operations.ContainsKey(schema.OperationName))
            .Select(schema => new BlueprintOperationTool(this, operations[schema.OperationName], schema))
            .ToList();

        // The schema provider holds the schemas of the app MapBlueprintApi had mapped last when MapBlueprintMcp was
        // called, so none of this app's means another app's API was mapped in between.
        if (tools.Count == 0 && operations.Count > 0)
        {
            throw new InvalidOperationException(
                $"No input schema was found for any operation of {this._app.GetType().Name}. Call MapBlueprintApi for it "
                + "before MapBlueprintMcp, with no other app's API mapped in between.");
        }

        return tools;
    }
}

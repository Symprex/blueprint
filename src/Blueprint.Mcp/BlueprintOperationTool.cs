using Blueprint.Model;
using Blueprint.Runtime.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Blueprint.Mcp;

/// <summary>
/// The MCP tool for one operation. A call makes the HTTP request the operation's endpoint answers from the call's
/// arguments and runs it through that endpoint in process, as the MCP request's caller, so the operation is bound,
/// validated, authenticated and authorised exactly as it is over HTTP (D22).
/// </summary>
/// <remarks>
/// The request is the MCP request's own as far as it can be: its scheme, host and path base (so links in the result
/// point at the HTTP API), its user, its <c>Authorization</c> header and its cancellation. Its other headers are not
/// copied: the operation is given only the headers its arguments supply. It has its own service scope, as an HTTP
/// request has.
/// </remarks>
internal sealed class BlueprintOperationTool : McpServerTool
{
    private readonly BlueprintMcpToolSet _toolSet;
    private readonly ApiOperationDescriptor _operation;
    private readonly OperationInputSchema _schema;
    private RouteEndpoint? _endpoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="BlueprintOperationTool"/> class.
    /// </summary>
    /// <param name="toolSet">The tool set that finds the operation's endpoint.</param>
    /// <param name="operation">The operation the tool runs.</param>
    /// <param name="schema">The operation's input schema.</param>
    public BlueprintOperationTool(BlueprintMcpToolSet toolSet, ApiOperationDescriptor operation, OperationInputSchema schema)
    {
        this._toolSet = toolSet;
        this._operation = operation;
        this._schema = schema;

        var isReadOnly = operation.TryGetFeatureData<OperationHttpMethodMetadataFeatureData>(out var methods)
            && methods.HttpMethodsArray.All(static m => HttpMethods.IsGet(m) || HttpMethods.IsHead(m));

        this.ProtocolTool = new Tool
        {
            Name = schema.OperationId,
            Description = Describe(schema),
            InputSchema = schema.Schema,
            Annotations = new ToolAnnotations { ReadOnlyHint = isReadOnly },
        };
    }

    /// <inheritdoc />
    public override Tool ProtocolTool { get; }

    /// <inheritdoc />
    public override IReadOnlyList<object> Metadata { get; } = [];

    /// <inheritdoc />
    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        var accessor = request.Services?.GetService<IHttpContextAccessor>();
        var caller = accessor?.HttpContext
                     ?? throw new InvalidOperationException(
                         "A Blueprint operation tool is called over HTTP only, and no HTTP request is being served.");

        var endpoint = this._endpoint ??= this._toolSet.FindEndpoint(this._operation, this._schema)
                                          ?? throw new InvalidOperationException(
                                              $"No endpoint is mapped for the operation {this._operation.Name}. Call MapBlueprintApi for its app.");

        var toolRequest = OperationToolRequest.Create(endpoint.RoutePattern, this._schema, request.Params?.Arguments, out var error);

        if (toolRequest is null)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = error! }],
            };
        }

        // The method the schema describes: of an operation answering GET and POST, the one that reads its body.
        var method = this._schema.HttpMethod
                     ?? endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.FirstOrDefault()
                     ?? HttpMethods.Get;

        // Its own service scope, as an HTTP request has: scoped services such as the authentication handlers serve the
        // request they were first asked for, so the MCP request's would challenge the MCP request, not the operation's.
        await using var scope = caller.RequestServices.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();

        var response = new HttpResponseFeature();
        using var responseBody = new ToolResponseBody(() => response.Headers.ContentType, OperationToolResult.MaxTextBytes);
        var context = CreateContext(caller, scope.ServiceProvider, endpoint, method, toolRequest, response, responseBody, cancellationToken);

        // A service reading the request from IHttpContextAccessor, as one may over HTTP, is given the operation's, not
        // the MCP request's. The MCP server itself does not read the accessor.
        accessor.HttpContext = context;

        try
        {
            await RunAsync(endpoint, context).ConfigureAwait(false);
            await context.Response.CompleteAsync().ConfigureAwait(false);
        }
        finally
        {
            accessor.HttpContext = caller;
        }

        return OperationToolResult.Create(context.Response.StatusCode, context.Response.ContentType, responseBody);
    }

    /// <summary>
    /// Runs the endpoint as the routing and authorisation middleware would: the ASP.NET Core authorisation policies its
    /// metadata requires (<c>RequireAuthorization</c>, <c>[Authorize]</c>) decide first, challenging or forbidding the
    /// caller as they would over HTTP, and the endpoint runs only when they let the caller through.
    /// </summary>
    private static async Task RunAsync(RouteEndpoint endpoint, HttpContext context)
    {
        var authorizeData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        var policies = endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>();

        if ((authorizeData.Count == 0 && policies.Count == 0) || endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await endpoint.RequestDelegate!(context).ConfigureAwait(false);
            return;
        }

        // An endpoint that asks for a policy nothing can evaluate is refused, as the authorisation middleware refuses it.
        var services = context.RequestServices;
        var policyProvider = services.GetService<IAuthorizationPolicyProvider>();
        var policyEvaluator = services.GetService<IPolicyEvaluator>();
        var resultHandler = services.GetService<IAuthorizationMiddlewareResultHandler>();

        if (policyProvider is null || policyEvaluator is null || resultHandler is null)
        {
            throw new InvalidOperationException(
                $"The endpoint {endpoint.DisplayName} requires authorisation, but the authorisation services are not "
                + "registered. Call services.AddAuthorization().");
        }

        var policy = await AuthorizationPolicy.CombineAsync(policyProvider, authorizeData, policies).ConfigureAwait(false);

        if (policy is null)
        {
            await endpoint.RequestDelegate!(context).ConfigureAwait(false);
            return;
        }

        var authenticateResult = await policyEvaluator.AuthenticateAsync(policy, context).ConfigureAwait(false);
        var authorizeResult = await policyEvaluator.AuthorizeAsync(policy, authenticateResult, context, context).ConfigureAwait(false);

        await resultHandler.HandleAsync(endpoint.RequestDelegate!, context, policy, authorizeResult).ConfigureAwait(false);
    }

    private static string? Describe(OperationInputSchema schema)
    {
        if (string.IsNullOrWhiteSpace(schema.Description))
        {
            return schema.Summary;
        }

        if (string.IsNullOrWhiteSpace(schema.Summary) || schema.Description.Contains(schema.Summary, StringComparison.Ordinal))
        {
            return schema.Description;
        }

        return string.Concat(schema.Summary, "\n\n", schema.Description);
    }

    private static DefaultHttpContext CreateContext(
        HttpContext caller,
        IServiceProvider services,
        RouteEndpoint endpoint,
        string method,
        OperationToolRequest toolRequest,
        HttpResponseFeature response,
        Stream responseBody,
        CancellationToken cancellationToken)
    {
        var headers = new HeaderDictionary { [HeaderNames.Accept] = "application/json" };

        if (caller.Request.Headers.Authorization is { Count: > 0 } authorization)
        {
            headers[HeaderNames.Authorization] = authorization;
        }

        foreach (var (name, value) in toolRequest.Headers)
        {
            headers[name] = value;
        }

        var body = Stream.Null;

        if (toolRequest.Body is { } bytes)
        {
            body = new MemoryStream(bytes, writable: false);
            headers[HeaderNames.ContentType] = "application/json; charset=utf-8";
            headers.ContentLength = bytes.Length;
        }

        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new HttpRequestFeature
        {
            Method = method,
            Protocol = caller.Request.Protocol,
            Scheme = caller.Request.Scheme,
            PathBase = caller.Request.PathBase,
            Path = toolRequest.Path,
            QueryString = toolRequest.QueryString.Value ?? string.Empty,
            Headers = headers,
            Body = body,
        });
        features.Set<IHttpResponseFeature>(response);
        features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(responseBody));
        features.Set<IHttpRequestLifetimeFeature>(new HttpRequestLifetimeFeature { RequestAborted = cancellationToken });
        features.Set<IHttpConnectionFeature>(new HttpConnectionFeature
        {
            ConnectionId = caller.Connection.Id,
            RemoteIpAddress = caller.Connection.RemoteIpAddress,
            RemotePort = caller.Connection.RemotePort,
            LocalIpAddress = caller.Connection.LocalIpAddress,
            LocalPort = caller.Connection.LocalPort,
        });

        var context = new DefaultHttpContext(features)
        {
            RequestServices = services,
            User = caller.User,
            TraceIdentifier = caller.TraceIdentifier,
        };

        context.Request.Host = caller.Request.Host;
        context.Request.RouteValues = toolRequest.RouteValues;
        context.SetEndpoint(endpoint);

        return context;
    }
}

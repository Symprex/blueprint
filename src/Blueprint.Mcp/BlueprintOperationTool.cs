using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Blueprint.Model;
using Blueprint.Runtime.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Blueprint.Mcp;

/// <summary>
/// The MCP tool for one operation. A call makes the HTTP request the operation's endpoint answers from the call's
/// arguments and runs it through that endpoint in process, signed in from the MCP request's credentials, so the
/// operation is bound, validated, authenticated and authorised exactly as it is over HTTP (D22).
/// </summary>
/// <remarks>
/// The request is the MCP request's own as far as it can be: its scheme, host and path base (so links in the result
/// point at the HTTP API), its <c>Authorization</c> header and its cancellation. Its other headers are not copied: the
/// operation is given only the headers its arguments supply. It has its own service scope, as an HTTP request has, and
/// is signed in from its own credentials by the host's default scheme, not given the MCP request's user.
/// </remarks>
internal sealed class BlueprintOperationTool : McpServerTool
{
    /// <summary>
    /// The <see cref="AppContext"/> switch with which a host has the authorisation middleware evaluate a policy for the
    /// endpoint rather than the request.
    /// </summary>
    private const string SuppressUseHttpContextAsAuthorizationResource =
        "Microsoft.AspNetCore.Authorization.SuppressUseHttpContextAsAuthorizationResource";

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

        var toolRequest = OperationToolRequest.Create(
            endpoint.RoutePattern,
            this._schema,
            request.Params?.Arguments,
            caller.Request.RouteValues,
            out var error);

        if (toolRequest is null)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = error! }],
            };
        }

        // A rate-limiting policy on the endpoint is applied by the rate-limiting middleware, which a tool call does not
        // go through and which offers no way to apply a named policy outside it. Rather than run the operation outside
        // its limit, the tool refuses to.
        if (endpoint.Metadata.GetMetadata<DisableRateLimitingAttribute>() is null
            && endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>() is { } rateLimiting)
        {
            var policy = rateLimiting.PolicyName is { } name ? $"rate limiting ('{name}')" : "rate limiting";

            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = $"The operation {this._operation.Name} requires {policy}, which a tool call cannot apply, so it is not run. Call it over HTTP." }],
            };
        }

        // The method the schema describes: of an operation answering GET and POST, the one that reads its body.
        var method = this._schema.HttpMethod
                     ?? endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.FirstOrDefault()
                     ?? HttpMethods.Get;

        // The operation's request runs in an execution context of its own, as an HTTP request does, inheriting none of
        // the MCP request's ambient state. So giving IHttpContextAccessor the operation's request, whose setter clears
        // the context it replaces, leaves the MCP request's in place for the middleware around it. The MCP request's
        // activity, so that the operation's traces nest under it, and its culture are carried in.
        var activity = Activity.Current;
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        Task<CallToolResult> run;

        using (ExecutionContext.SuppressFlow())
        {
            run = Task.Run(() =>
            {
                Activity.Current = activity;
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = uiCulture;

                return RunRequestAsync(accessor, caller, endpoint, this._toolSet.Routing, method, toolRequest, cancellationToken);
            });
        }

        return await run.ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the operation's request through its endpoint, from start to end, and gives the response it wrote as the
    /// call's result.
    /// </summary>
    private static async Task<CallToolResult> RunRequestAsync(
        IHttpContextAccessor accessor,
        HttpContext caller,
        RouteEndpoint endpoint,
        HttpRouteProbe routing,
        string method,
        OperationToolRequest toolRequest,
        CancellationToken cancellationToken)
    {
        // Its own service scope, as an HTTP request has: scoped services such as the authentication handlers serve the
        // request they were first asked for, so the MCP request's would challenge the MCP request, not the operation's.
        var scope = caller.RequestServices.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<BlueprintOperationTool>>();

        // A server sends no body in answer to HEAD, whatever the endpoint writes.
        using var response = new ToolResponse(OperationToolResult.MaxTextBytes, discardBody: HttpMethods.IsHead(method));
        var context = CreateContext(caller, scope.ServiceProvider, endpoint, method, toolRequest, response, cancellationToken);

        // A service reading the request from IHttpContextAccessor, as one may over HTTP, is given the operation's, not the
        // MCP request's, until the operation's request has ended: its OnCompleted callbacks run and its scope is
        // disposed. The MCP server itself does not read the accessor.
        accessor.HttpContext = context;

        try
        {
            try
            {
                // Signed in before the route is matched only where the host authenticates before it routes, so a
                // constraint or matcher policy reading the user sees what it would over HTTP: the user, or no one.
                var authenticateBeforeRouting = RoutingOrderMatcherPolicy.AuthenticatedBeforeRouting(caller);

                if (authenticateBeforeRouting)
                {
                    await AuthenticateAsync(context).ConfigureAwait(false);
                }

                // A request no HTTP route would match, as a value its route constraints refuse, gets routing's 404. The
                // constraints are given the operation's request and services, as routing would give them, not the MCP
                // request's: one may read the method, path, query or headers.
                var policyFactory = context.RequestServices.GetRequiredService<ParameterPolicyFactory>();

                if (!OperationToolRequest.MatchesConstraints(endpoint.RoutePattern, toolRequest.RouteValues, context, policyFactory, out var refused))
                {
                    return NotFound($"The argument '{refused}' does not satisfy its route constraints, so no HTTP route would match the request.");
                }

                // Nor at a host the endpoint does not allow (RequireHost), which routing's host matching refuses.
                if (!OperationToolRequest.MatchesHost(endpoint.Metadata.GetMetadata<IHostMetadata>(), context.Request))
                {
                    return NotFound($"The operation {endpoint.DisplayName} is not served at the host {context.Request.Host}, so no HTTP route would match the request.");
                }

                // Nor where HTTP routing would select another endpoint, as a literal route that outranks the operation's
                // ({Name} given "special" where /special is mapped too): over HTTP the operation never sees that request.
                var routed = await routing.FindAsync(context).ConfigureAwait(false);

                if (!ReferenceEquals(routed, endpoint))
                {
                    return routed is null
                        ? NotFound($"No HTTP route reaches the operation {endpoint.DisplayName} at {context.Request.Path}.")
                        : new CallToolResult
                        {
                            IsError = true,
                            Content = [new TextContentBlock { Text = $"HTTP routing sends a request for {context.Request.Path} to {routed.DisplayName}, not to the operation {endpoint.DisplayName}, so the operation is not run. Call that operation's tool instead." }],
                        };
                }

                if (!authenticateBeforeRouting)
                {
                    await AuthenticateAsync(context).ConfigureAwait(false);
                }

                await RunAsync(endpoint, context).ConfigureAwait(false);
                await context.Response.CompleteAsync().ConfigureAwait(false);

                return OperationToolResult.Create(context.Response.StatusCode, context.Response.ContentType, response.Captured);
            }
            finally
            {
                // As a server does once it has sent the response, which disposes what the request registered for disposal.
                await response.RunCompletedCallbacksAsync(logger).ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                await scope.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                // As a server clears it once a request has ended. This execution context is the operation's alone.
                accessor.HttpContext = null;
            }
        }
    }

    /// <summary>
    /// Runs the endpoint as the authorisation middleware would: the policy it applies to the endpoint decides first,
    /// challenging or forbidding the caller as it would over HTTP, and the endpoint runs only when it lets the caller
    /// through. That policy combines the endpoint's policies and <c>[Authorize]</c> data, or is the fallback policy when
    /// it has neither, with the requirements its metadata supplies (<see cref="IAuthorizationRequirementData"/>).
    /// </summary>
    private static async Task RunAsync(RouteEndpoint endpoint, HttpContext context)
    {
        var metadata = endpoint.Metadata;
        var authorizeData = metadata.GetOrderedMetadata<IAuthorizeData>();
        var policies = metadata.GetOrderedMetadata<AuthorizationPolicy>();
        var requirementData = metadata.GetOrderedMetadata<IAuthorizationRequirementData>();
        var services = context.RequestServices;

        // The endpoint runs within the host's global rate limiter, which the rate-limiting middleware applies once the
        // authorisation middleware has let the caller through.
        var run = GlobalRateLimiting.Apply(endpoint, services);

        if (services.GetService<IAuthorizationPolicyProvider>() is not { } policyProvider)
        {
            // A host without authorisation has no policy to apply, but an endpoint that asks for one is refused, as the
            // authorisation middleware refuses it, rather than run unguarded.
            if (authorizeData.Count > 0 || policies.Count > 0 || requirementData.Count > 0)
            {
                throw new InvalidOperationException(
                    $"The endpoint {endpoint.DisplayName} requires authorisation, but the authorisation services are not "
                    + "registered. Call services.AddAuthorization().");
            }

            await run(context).ConfigureAwait(false);
            return;
        }

        var policy = await AuthorizationPolicy.CombineAsync(policyProvider, authorizeData, policies).ConfigureAwait(false);

        if (requirementData.Count > 0)
        {
            var requirements = new AuthorizationPolicyBuilder();

            foreach (var data in requirementData)
            {
                requirements.AddRequirements([.. data.GetRequirements()]);
            }

            policy = policy is null ? requirements.Build() : AuthorizationPolicy.Combine(policy, requirements.Build());
        }

        if (policy is null)
        {
            await run(context).ConfigureAwait(false);
            return;
        }

        var policyEvaluator = services.GetRequiredService<IPolicyEvaluator>();
        var resultHandler = services.GetRequiredService<IAuthorizationMiddlewareResultHandler>();

        // The policy's schemes authenticate the caller even where anyone is allowed, which skips only the authorisation.
        var authenticateResult = await policyEvaluator.AuthenticateAsync(policy, context).ConfigureAwait(false);

        if (metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await run(context).ConfigureAwait(false);
            return;
        }

        // The resource the policy is evaluated for, as the authorisation middleware chooses it: the endpoint where the host
        // has switched to it, else the request.
        object resource = AppContext.TryGetSwitch(SuppressUseHttpContextAsAuthorizationResource, out var useEndpoint) && useEndpoint
            ? endpoint
            : context;

        var authorizeResult = await policyEvaluator.AuthorizeAsync(policy, authenticateResult, context, resource).ConfigureAwait(false);

        await resultHandler.HandleAsync(run, context, policy, authorizeResult).ConfigureAwait(false);
    }

    /// <summary>
    /// Signs the operation's request in as the authentication middleware signs in an HTTP request: with the host's default
    /// authenticate scheme, from the request's own credentials (its <c>Authorization</c> header), or as no one.
    /// </summary>
    /// <remarks>
    /// The MCP request's user is not carried over. The MCP endpoint may sign its caller in with a scheme of its own, which
    /// the API's endpoints do not use, and an operation run as that user could pass a policy the same request fails over
    /// HTTP. A caller signed in by anything but the <c>Authorization</c> header, such as a cookie, is therefore anonymous
    /// to the operation.
    /// </remarks>
    private static async Task AuthenticateAsync(HttpContext context)
    {
        context.User = new ClaimsPrincipal(new ClaimsIdentity());

        if (context.RequestServices.GetService<IAuthenticationSchemeProvider>() is not { } schemes
            || await schemes.GetDefaultAuthenticateSchemeAsync().ConfigureAwait(false) is not { } scheme)
        {
            return;
        }

        var result = await context.AuthenticateAsync(scheme.Name).ConfigureAwait(false);

        if (result.Principal is { } principal)
        {
            context.User = principal;
        }
    }

    /// <summary>
    /// Makes the result of a call no HTTP route would reach: routing's 404, as a tool error saying why.
    /// </summary>
    private static CallToolResult NotFound(string reason)
    {
        return new CallToolResult
        {
            IsError = true,
            Meta = new JsonObject { [OperationToolResult.HttpStatusMetaKey] = StatusCodes.Status404NotFound },
            Content = [new TextContentBlock { Text = reason }],
        };
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
        ToolResponse response,
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

            // Decoded, as the feature holds it; PathString's conversion to a string would escape it.
            PathBase = caller.Request.PathBase.Value ?? string.Empty,
            Path = toolRequest.Path,
            QueryString = toolRequest.QueryString.Value ?? string.Empty,
            Headers = headers,
            Body = body,
        });
        features.Set<IHttpResponseFeature>(response);
        features.Set<IHttpResponseBodyFeature>(response);
        features.Set<IHttpRequestLifetimeFeature>(new HttpRequestLifetimeFeature { RequestAborted = cancellationToken });
        features.Set<IHttpConnectionFeature>(new HttpConnectionFeature
        {
            ConnectionId = caller.Connection.Id,
            RemoteIpAddress = caller.Connection.RemoteIpAddress,
            RemotePort = caller.Connection.RemotePort,
            LocalIpAddress = caller.Connection.LocalIpAddress,
            LocalPort = caller.Connection.LocalPort,
        });

        // The client certificate the caller's connection presented, which an HTTP request on that connection carries. Only
        // the certificate: the operation's request cannot renegotiate the MCP request's connection for one.
        if (caller.Connection.ClientCertificate is { } certificate)
        {
            features.Set<ITlsConnectionFeature>(new TlsConnectionFeature { ClientCertificate = certificate });
        }

        var context = new DefaultHttpContext(features)
        {
            RequestServices = services,
            TraceIdentifier = caller.TraceIdentifier,
        };

        context.Request.Host = caller.Request.Host;
        context.Request.RouteValues = toolRequest.RouteValues;
        context.SetEndpoint(endpoint);

        return context;
    }
}

using Microsoft.Extensions.Primitives;

namespace Blueprint.Mcp;

/// <summary>
/// Finds the endpoint HTTP routing selects for a request, by running the host's own routing over the host's endpoints:
/// its matcher, with route precedence, constraints and every matcher policy (host, method and the like), as an HTTP
/// request for the same URL is routed. A tool call runs through the endpoint of its own operation, so it is run only
/// when routing would select that endpoint too, and never where HTTP would send the request to another operation.
/// </summary>
/// <remarks>
/// Routing selects from copies of the host's endpoints whose request delegates do nothing, so matching runs nothing:
/// not even an endpoint that routing short-circuits, which it runs as soon as it has selected it.
/// </remarks>
internal sealed class HttpRouteProbe
{
    // The key UseRouting stores the route builder whose data sources its routing middleware matches against.
    private const string EndpointRouteBuilderKey = "__EndpointRouteBuilder";

    private readonly RequestDelegate _route;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpRouteProbe"/> class.
    /// </summary>
    /// <param name="services">The host's services, whose <see cref="EndpointDataSource"/> composes every endpoint mapped.</param>
    public HttpRouteProbe(IServiceProvider services)
    {
        var app = new ApplicationBuilder(services);
        app.UseRouting();

        if (!app.Properties.TryGetValue(EndpointRouteBuilderKey, out var value) || value is not IEndpointRouteBuilder routes)
        {
            throw new InvalidOperationException("UseRouting did not give the route builder its routing middleware matches against.");
        }

        routes.DataSources.Add(new ProbeEndpointDataSource(services.GetRequiredService<EndpointDataSource>()));
        app.Run(static _ => Task.CompletedTask);

        this._route = app.Build();
    }

    /// <summary>
    /// Finds the endpoint HTTP routing selects for <paramref name="request"/>.
    /// </summary>
    /// <param name="request">The request, whose method, URL and headers routing reads, and whose services its constraints are given.</param>
    /// <returns>The host's endpoint routing selects, or <see langword="null"/> when it selects none of them.</returns>
    public async Task<Endpoint?> FindAsync(HttpContext request)
    {
        // A request of its own, so routing sets the endpoint and route values on it, not on the operation's request.
        var probe = new DefaultHttpContext { RequestServices = request.RequestServices };
        probe.Request.Method = request.Request.Method;
        probe.Request.Protocol = request.Request.Protocol;
        probe.Request.Scheme = request.Request.Scheme;
        probe.Request.Host = request.Request.Host;
        probe.Request.PathBase = request.Request.PathBase;
        probe.Request.Path = request.Request.Path;
        probe.Request.QueryString = request.Request.QueryString;

        foreach (var (name, values) in request.Request.Headers)
        {
            probe.Request.Headers[name] = values;
        }

        await this._route(probe).ConfigureAwait(false);

        return probe.GetEndpoint()?.Metadata.GetMetadata<ProbedEndpoint>()?.Endpoint;
    }

    /// <summary>
    /// Marks a copy of a host endpoint with the endpoint it copies.
    /// </summary>
    private sealed record ProbedEndpoint(Endpoint Endpoint);

    /// <summary>
    /// The host's route endpoints, copied with request delegates that do nothing, made again whenever the host's change.
    /// </summary>
    private sealed class ProbeEndpointDataSource(EndpointDataSource host) : EndpointDataSource
    {
        private static readonly RequestDelegate Nothing = static _ => Task.CompletedTask;

        // Replaced whole, never changed, so a reader on another thread sees a source and its copies together.
        private Snapshot? _copies;

        public override IReadOnlyList<Endpoint> Endpoints
        {
            get
            {
                var source = host.Endpoints;

                if (Volatile.Read(ref this._copies) is { } copies && ReferenceEquals(copies.Source, source))
                {
                    return copies.Copies;
                }

                var made = source
                    .OfType<RouteEndpoint>()
                    .Select(static e => (Endpoint)new RouteEndpoint(
                        Nothing,
                        e.RoutePattern,
                        e.Order,
                        new EndpointMetadataCollection([.. e.Metadata, new ProbedEndpoint(e)]),
                        e.DisplayName))
                    .ToList();

                Volatile.Write(ref this._copies, new Snapshot(source, made));

                return made;
            }
        }

        public override IChangeToken GetChangeToken()
        {
            return host.GetChangeToken();
        }

        private sealed record Snapshot(IReadOnlyList<Endpoint> Source, IReadOnlyList<Endpoint> Copies);
    }
}

using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Blueprint.Mcp;

/// <summary>
/// Applies the host's global rate limiter (<see cref="RateLimiterOptions.GlobalLimiter"/>) to an operation's request, as
/// the rate-limiting middleware applies it to an HTTP request, so a quota it keeps per path, user or anything else it
/// partitions by is spent by a tool call as by the HTTP request the call makes.
/// </summary>
/// <remarks>
/// It runs once the caller is authorised and before the endpoint, where <c>UseRateLimiter</c> runs after
/// <c>UseAuthorization</c>. The MCP endpoint must be exempt from it (<c>DisableRateLimiting</c>), or a call would hold
/// one permit while it waits for another (<see cref="NestsWithinMcpRequest"/>).
/// </remarks>
internal static class GlobalRateLimiting
{
    /// <summary>
    /// Gets whether the host's global rate limiter would be asked for a permit for the operation's request while the MCP
    /// request, which it limits too, holds one. Where both fall in one concurrency partition the operation's request
    /// would wait for the MCP request to end, which waits for it, so such a call is refused instead.
    /// </summary>
    /// <param name="endpoint">The operation's endpoint.</param>
    /// <param name="caller">The MCP request.</param>
    /// <returns>
    /// <see langword="true"/> when the host has a global limiter, the operation's endpoint does not disable rate limiting
    /// and the MCP endpoint does not either.
    /// </returns>
    public static bool NestsWithinMcpRequest(RouteEndpoint endpoint, HttpContext caller)
    {
        return endpoint.Metadata.GetMetadata<DisableRateLimitingAttribute>() is null
               && caller.GetEndpoint()?.Metadata.GetMetadata<DisableRateLimitingAttribute>() is null
               && caller.RequestServices.GetService<IOptions<RateLimiterOptions>>()?.Value.GlobalLimiter is not null;
    }

    /// <summary>
    /// Gives the endpoint's request delegate, run within the host's global rate limiter when it has one and the endpoint
    /// does not disable rate limiting.
    /// </summary>
    /// <param name="endpoint">The operation's endpoint.</param>
    /// <param name="services">The operation request's services.</param>
    /// <returns>The delegate to run the endpoint with.</returns>
    public static RequestDelegate Apply(RouteEndpoint endpoint, IServiceProvider services)
    {
        var next = endpoint.RequestDelegate!;

        if (endpoint.Metadata.GetMetadata<DisableRateLimitingAttribute>() is not null
            || services.GetService<IOptions<RateLimiterOptions>>()?.Value is not { GlobalLimiter: { } limiter } options)
        {
            return next;
        }

        return async context =>
        {
            var lease = limiter.AttemptAcquire(context);

            if (!lease.IsAcquired)
            {
                // As the middleware does, the request waits in the limiter's queue, if it has one, until it is given a
                // permit or the caller goes.
                lease.Dispose();

                try
                {
                    lease = await limiter.AcquireAsync(context, cancellationToken: context.RequestAborted).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                {
                    return;
                }
            }

            using (lease)
            {
                if (lease.IsAcquired)
                {
                    await next(context).ConfigureAwait(false);
                    return;
                }

                context.Response.StatusCode = options.RejectionStatusCode;

                if (options.OnRejected is { } onRejected)
                {
                    await onRejected(new OnRejectedContext { HttpContext = context, Lease = lease }, context.RequestAborted).ConfigureAwait(false);
                }
            }
        };
    }
}

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Routing.Matching;

namespace Blueprint.Mcp;

/// <summary>
/// Records, as routing matches a request to a <c>MapBlueprintMcp</c> endpoint, whether the host authenticated that
/// request before routing it. The host's routing gives a constraint or matcher policy the user only when it does, so a
/// tool call's route is matched as the signed-in user only in a host that authenticates first, as HTTP's would be.
/// </summary>
/// <remarks>
/// <c>UseAuthentication</c> gives a request its <see cref="IAuthenticationFeature"/> before it calls the rest of the
/// pipeline, so the feature is there as routing runs only when authentication comes first.
/// </remarks>
internal sealed class RoutingOrderMatcherPolicy : MatcherPolicy, IEndpointSelectorPolicy
{
    private static readonly object AuthenticatedBeforeRoutingKey = new();

    /// <inheritdoc />
    public override int Order => 0;

    /// <summary>
    /// Gets whether the host authenticated <paramref name="caller"/>, an MCP request, before routing it.
    /// </summary>
    /// <param name="caller">The MCP request.</param>
    /// <returns>
    /// <see langword="true"/> when authentication ran before routing; <see langword="false"/> when it ran after, did not
    /// run, or routing did not record it.
    /// </returns>
    public static bool AuthenticatedBeforeRouting(HttpContext caller)
    {
        return caller.Items.TryGetValue(AuthenticatedBeforeRoutingKey, out var value) && value is true;
    }

    /// <inheritdoc />
    public bool AppliesToEndpoints(IReadOnlyList<Endpoint> endpoints)
    {
        return endpoints.Any(static e => e.Metadata.GetMetadata<BlueprintMcpToolSet>() is not null);
    }

    /// <inheritdoc />
    public Task ApplyAsync(HttpContext httpContext, CandidateSet candidates)
    {
        httpContext.Items[AuthenticatedBeforeRoutingKey] = httpContext.Features.Get<IAuthenticationFeature>() is not null;

        return Task.CompletedTask;
    }
}

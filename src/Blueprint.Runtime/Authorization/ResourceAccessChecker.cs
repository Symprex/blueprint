using System.Security.Claims;
using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.Authorization;

/// <summary>
/// The default <see cref="IResourceAccessChecker"/>: it expands a partial key through the registered
/// <see cref="IResourceKeyExpander"/>s, and allows a demand when a grant from any registered
/// <see cref="IResourceGrantSource"/> covers any of its keys.
/// </summary>
/// <remarks>
/// <para>
/// Scoped, because the expanders and sources it is given may be. They are materialised once, in registration order, which
/// is the order expanders are asked in.
/// </para>
/// <para>
/// The key is expanded before any grant is read, so a partial key no expander handles is reported as such whatever the
/// caller holds, rather than hidden behind a missing grant.
/// </para>
/// </remarks>
internal sealed class ResourceAccessChecker : IResourceAccessChecker
{
    private readonly IResourceKeyExpander[] _expanders;
    private readonly IResourceGrantSource[] _grantSources;
    private readonly ILogger<ResourceAccessChecker> _logger;

    /// <summary>
    /// Initialises a new instance of the <see cref="ResourceAccessChecker"/> class.
    /// </summary>
    /// <param name="expanders">The registered key expanders, in registration order.</param>
    /// <param name="grantSources">The registered grant sources.</param>
    /// <param name="logger">Logs a partial key that no expander handles, which is a misconfiguration.</param>
    public ResourceAccessChecker(
        IEnumerable<IResourceKeyExpander> expanders,
        IEnumerable<IResourceGrantSource> grantSources,
        ILogger<ResourceAccessChecker> logger)
    {
        this._expanders = expanders.ToArray();
        this._grantSources = grantSources.ToArray();
        this._logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<ExecutionAllowed> HasAccessAsync(
        ApiOperationContext context,
        ClaimsPrincipal principal,
        ResourceDemand demand,
        ResolvedScopePair scopePair,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(principal);

        if (demand.Operation is null)
        {
            throw new ArgumentException("A resource demand must name the operation being authorised; default(ResourceDemand) names none.", nameof(demand));
        }

        var key = demand.ResourceKey;
        IReadOnlyList<ResourceKey>? expandedKeys = null;

        if (!key.IsAbsolute)
        {
            if (key.Value.Length == 0)
            {
                return Deny($"{demand.Operation.OperationType.Name} demands an empty resource key, which no grant can cover");
            }

            expandedKeys = await this.ExpandAsync(key, context, cancellationToken).ConfigureAwait(false);

            if (expandedKeys is null)
            {
                this._logger.LogWarning(
                    "No IResourceKeyExpander handled the partial resource key {ResourceKey} demanded by {OperationType}, so it was denied",
                    key.Value,
                    demand.Operation.OperationType.Name);

                return Deny(
                    $"{demand.Operation.OperationType.Name} demands the partial resource key '{key.Value}', but no IResourceKeyExpander handled it. " +
                    "A partial key (one not starting with '/') must be expanded to absolute keys before a grant can cover it; register an expander for it with AddResourceKeyExpander");
            }
        }

        if (this._grantSources.Length == 0)
        {
            return Deny($"No IResourceGrantSource is registered, so no grant covers '{key.Value}' for {demand.Operation.OperationType.Name}");
        }

        foreach (var source in this._grantSources)
        {
            var grants = await source.GetGrantsAsync(context, principal, demand.Operation, scopePair, cancellationToken).ConfigureAwait(false);

            if (grants is not null && CoversAny(grants, key, expandedKeys))
            {
                return ExecutionAllowed.Yes;
            }
        }

        return Deny(expandedKeys is null
            ? $"No grant held for {demand.Operation.OperationType.Name} under {scopePair.Noun}/{scopePair.Verb} covers '{key.Value}'"
            : $"No grant held for {demand.Operation.OperationType.Name} under {scopePair.Noun}/{scopePair.Verb} covers '{key.Value}', expanded to [{string.Join(", ", expandedKeys)}]");
    }

    /// <summary>
    /// Whether any grant covers the demanded key, or, for a partial key, any of the keys it was expanded to: the partial
    /// key itself is never matched, as no grant could cover it.
    /// </summary>
    private static bool CoversAny(ResourceGrants grants, ResourceKey key, IReadOnlyList<ResourceKey>? expandedKeys)
    {
        if (expandedKeys is null)
        {
            return grants.Covers(key);
        }

        for (var i = 0; i < expandedKeys.Count; i++)
        {
            if (grants.Covers(expandedKeys[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static ExecutionAllowed Deny(string reason)
    {
        return ExecutionAllowed.No(reason, ResourceKeyAuthorizationProvider.DeniedMessage, ExecutionAllowedFailureType.Authorisation);
    }

    /// <summary>
    /// Asks the expanders in registration order, returning the first answer that is not <see langword="null"/>: later
    /// expanders are not asked, so none can override an earlier one's placement of the key.
    /// </summary>
    private async ValueTask<IReadOnlyList<ResourceKey>?> ExpandAsync(ResourceKey partialKey, ApiOperationContext context, CancellationToken cancellationToken)
    {
        foreach (var expander in this._expanders)
        {
            var expanded = await expander.ExpandAsync(partialKey, context, cancellationToken).ConfigureAwait(false);

            if (expanded is not null)
            {
                return expanded;
            }
        }

        return null;
    }
}

using System.Security.Claims;
using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// A sample <see cref="IResourceAuthorizationProvider"/> that demonstrates resource-level
/// authorization by checking for a <c>scope</c> claim matching the requested resource scope pairs
/// and optionally performing hierarchical resource key template expansion for instance-level
/// authorization checks.
/// </summary>
/// <remarks>
/// <para>
/// In a production application this would integrate with your permission system (e.g. checking
/// a permissions database, evaluating RBAC policies, or calling an external authorization service
/// such as OpenFGA or OPA). This sample uses simple claim-matching to illustrate the contract.
/// </para>
/// <para>
/// When a scope pair carries a <see cref="ScopePair.ResourceKeyTemplate"/>, this provider
/// uses <see cref="ResourceKeyTemplateExpander"/> to expand the template (substituting
/// <c>{PropertyName}</c> tokens from the operation and <c>{Auth:ClaimType}</c> tokens from
/// the principal's claims), then checks whether the caller holds a matching hierarchical
/// resource key claim.
/// </para>
/// </remarks>
public sealed class SampleResourceAuthorizationProvider(ILogger<SampleResourceAuthorizationProvider> logger) : IResourceAuthorizationProvider
{
    /// <inheritdoc />
    public Task<bool> IsAuthorizedAsync(
        ApiOperationContext operationContext,
        ClaimsPrincipal principal,
        IReadOnlyList<ScopePair> requiredScopePairs,
        CancellationToken cancellationToken)
    {
        // Any-of semantics: authorized if the principal has ANY matching scope claim
        foreach (var scopePair in requiredScopePairs)
        {
            var requiredScope = $"{scopePair.Noun}:{scopePair.Verb}";

            if (!principal.HasClaim("scope", requiredScope))
            {
                continue;
            }

            // When a resource key template is present, expand it and verify the caller holds
            // a matching hierarchical resource key claim. This is the S365-style pattern where
            // permissions are scoped to specific resource instances (e.g. /Tenants/T-1/Templates/T-99).
            if (scopePair.ResourceKeyTemplate is not null)
            {
                var expandedKey = ResourceKeyTemplateExpander.Expand(
                    scopePair.ResourceKeyTemplate,
                    operationContext.Operation,
                    principal);

                // Check if caller holds a claim that is an ancestor of or equal to the expanded key
                var hasResourceClaim = principal.Claims
                    .Where(static c => c.Type == "resource_key")
                    .Any(c => expandedKey.StartsWith(c.Value, StringComparison.OrdinalIgnoreCase));

                if (!hasResourceClaim)
                {
                    logger.LogInformation(
                        "Authorization denied: caller lacks resource key claim for {ExpandedKey}",
                        expandedKey);

                    return Task.FromResult(false);
                }

                logger.LogDebug(
                    "Authorization granted for scope {Scope} with resource key {ResourceKey}",
                    requiredScope,
                    expandedKey);

                return Task.FromResult(true);
            }

            // Simple scope-pair check without resource key template
            logger.LogDebug(
                "Authorization granted for scope {Scope}",
                requiredScope);

            return Task.FromResult(true);
        }

        logger.LogInformation(
            "Authorization denied: principal lacks required scopes [{Scopes}]",
            string.Join(", ", requiredScopePairs.Select(static sp => $"{sp.Noun}:{sp.Verb}")));

        return Task.FromResult(false);
    }
}

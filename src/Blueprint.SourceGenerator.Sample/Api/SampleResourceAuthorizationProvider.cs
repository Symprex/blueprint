using System.Security.Claims;
using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// A sample <see cref="IResourceAuthorizationProvider"/> that demonstrates resource-level
/// authorization by checking for a <c>scope</c> claim matching the requested resource scope pairs
/// and optionally verifying hierarchical resource key claims for instance-level authorization.
/// </summary>
/// <remarks>
/// <para>
/// In a production application this would integrate with your permission system (e.g. checking
/// a permissions database, evaluating RBAC policies, or calling an external authorization service
/// such as OpenFGA or OPA). This sample uses simple claim-matching to illustrate the contract.
/// </para>
/// <para>
/// When a scope pair carries a non-null <see cref="ResolvedScopePair.ExpandedResourceKey"/>,
/// this provider checks whether the caller holds a matching hierarchical resource key claim.
/// The generated pipeline has already expanded template tokens at compile time, so no
/// reflection or regex expansion is needed here.
/// </para>
/// </remarks>
public sealed class SampleResourceAuthorizationProvider(ILogger<SampleResourceAuthorizationProvider> logger) : IResourceAuthorizationProvider
{
    /// <inheritdoc />
    public Task<bool> IsAuthorizedAsync(
        ApiOperationContext operationContext,
        ClaimsPrincipal principal,
        IReadOnlyList<ResolvedScopePair> resolvedScopePairs,
        CancellationToken cancellationToken)
    {
        // Any-of semantics: authorized if the principal has ANY matching scope claim
        foreach (var scopePair in resolvedScopePairs)
        {
            var requiredScope = $"{scopePair.Noun}:{scopePair.Verb}";

            if (!principal.HasClaim("scope", requiredScope))
            {
                continue;
            }

            // When an expanded resource key is present, verify the caller holds
            // a matching hierarchical resource key claim.
            if (scopePair.ExpandedResourceKey is not null)
            {
                var hasResourceClaim = principal.Claims
                    .Where(static c => c.Type == "resource_key")
                    .Any(c => scopePair.ExpandedResourceKey.StartsWith(c.Value, StringComparison.OrdinalIgnoreCase));

                if (!hasResourceClaim)
                {
                    logger.LogInformation(
                        "Authorization denied: caller lacks resource key claim for {ExpandedKey}",
                        scopePair.ExpandedResourceKey);

                    return Task.FromResult(false);
                }

                logger.LogDebug(
                    "Authorization granted for scope {Scope} with resource key {ResourceKey}",
                    requiredScope,
                    scopePair.ExpandedResourceKey);

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
            string.Join(", ", resolvedScopePairs.Select(static sp => $"{sp.Noun}:{sp.Verb}")));

        return Task.FromResult(false);
    }
}

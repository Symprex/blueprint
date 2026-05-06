using System.Security.Claims;
using Blueprint.Authorization;
using Blueprint.Model;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// A sample <see cref="IResourceAuthorizationProvider"/> that demonstrates resource-level
/// authorization by checking for a <c>scope</c> claim matching the requested resource scope pairs.
/// </summary>
/// <remarks>
/// In a production application this would integrate with your permission system (e.g. checking
/// a permissions database, evaluating RBAC policies, or calling an external authorization service
/// such as OpenFGA or OPA). This sample uses simple claim-matching to illustrate the contract.
/// </remarks>
public sealed class SampleResourceAuthorizationProvider(ILogger<SampleResourceAuthorizationProvider> logger) : IResourceAuthorizationProvider
{
    /// <inheritdoc />
    public Task<bool> IsAuthorizedAsync(
        ClaimsPrincipal principal,
        IReadOnlyList<ScopePair> requiredScopePairs,
        CancellationToken cancellationToken)
    {
        // Any-of semantics: authorized if the principal has ANY matching scope claim
        foreach (var scopePair in requiredScopePairs)
        {
            var requiredScope = $"{scopePair.Noun}:{scopePair.Verb}";

            if (principal.HasClaim("scope", requiredScope))
            {
                logger.LogDebug(
                    "Authorization granted for scope {Scope}",
                    requiredScope);

                return Task.FromResult(true);
            }
        }

        logger.LogInformation(
            "Authorization denied: principal lacks required scopes [{Scopes}]",
            string.Join(", ", requiredScopePairs.Select(static sp => $"{sp.Noun}:{sp.Verb}")));

        return Task.FromResult(false);
    }
}

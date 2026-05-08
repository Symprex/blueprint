using System.Security.Claims;
using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// A sample <see cref="IResourceAuthorizationProvider"/> that demonstrates resource-level
/// authorization by checking for a <c>scope</c> claim matching the requested resource scope pairs
/// and optionally performing ownership verification against operation properties.
/// </summary>
/// <remarks>
/// <para>
/// In a production application this would integrate with your permission system (e.g. checking
/// a permissions database, evaluating RBAC policies, or calling an external authorization service
/// such as OpenFGA or OPA). This sample uses simple claim-matching to illustrate the contract.
/// </para>
/// <para>
/// The <see cref="ApiOperationContext"/> parameter enables ownership-based authorization patterns
/// where the provider inspects operation properties (e.g. a <c>TenantId</c> property) and compares
/// them to the caller's claims. This is analogous to legacy Blueprint's resource key template pattern
/// (<c>[Resource("noun", "verb", "/Tenants/{Auth:AccountId}")]</c>) but resolved entirely at the
/// provider level without needing template expansion in the pipeline.
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

            if (principal.HasClaim("scope", requiredScope))
            {
                // Optionally perform ownership check: verify the caller's tenant claim
                // matches the operation's TenantId property (if present).
                // This demonstrates the resource key template pattern from legacy Blueprint:
                //   [Resource("noun", "verb", "/Tenants/{Auth:AccountId}")]
                var tenantProperty = operationContext.Operation.GetType().GetProperty("TenantId");

                if (tenantProperty != null)
                {
                    var operationTenantId = tenantProperty.GetValue(operationContext.Operation)?.ToString();
                    var callerTenantId = principal.FindFirst("tenant_id")?.Value;

                    if (operationTenantId != null && callerTenantId != null &&
                        !string.Equals(operationTenantId, callerTenantId, StringComparison.OrdinalIgnoreCase))
                    {
                        logger.LogInformation(
                            "Authorization denied: tenant mismatch (operation={OperationTenant}, caller={CallerTenant})",
                            operationTenantId,
                            callerTenantId);

                        return Task.FromResult(false);
                    }
                }

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

using System.Security.Claims;
using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Tests.Integration;

/// <summary>
/// An <see cref="IResourceAuthorizationProvider"/> that authorises every request. It reproduces what a missing provider
/// did before Symprex/blueprint#41, for hosts whose tests are about something other than scope pairs. A test about
/// scope-pair authorisation registers a real provider instead.
/// </summary>
public sealed class PermitAllResourceAuthorizationProvider : IResourceAuthorizationProvider
{
    /// <inheritdoc />
    public Task<bool> IsAuthorizedAsync(
        ApiOperationContext operationContext,
        ClaimsPrincipal principal,
        IReadOnlyList<ResolvedScopePair> resolvedScopePairs,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(true);
    }
}

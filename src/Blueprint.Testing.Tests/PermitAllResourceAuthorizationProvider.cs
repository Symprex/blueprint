using System.Security.Claims;
using Blueprint.Authorization;
using Blueprint.Runtime.Exec;

namespace Blueprint.Testing.Tests;

/// <summary>
/// An <see cref="IResourceAuthorizationProvider"/> that authorises every request. It reproduces what a missing provider
/// did before Symprex/blueprint#41, for hosts whose tests are about something other than scope pairs. A test about
/// scope-pair authorisation registers a real provider instead.
/// </summary>
internal sealed class PermitAllResourceAuthorizationProvider : IResourceAuthorizationProvider
{
    /// <inheritdoc />
    public ValueTask<ExecutionAllowed> AuthorizeAsync(
        ApiOperationContext operationContext,
        ClaimsPrincipal principal,
        ResourceAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        return new ValueTask<ExecutionAllowed>(ExecutionAllowed.Yes);
    }
}

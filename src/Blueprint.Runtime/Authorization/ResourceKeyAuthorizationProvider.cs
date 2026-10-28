using System.Security.Claims;
using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.Authorization;

/// <summary>
/// An <see cref="IResourceAuthorizationProvider"/> that decides each scope pair by the caller's grants on its resource
/// key: hierarchical, instance-level authorisation such as "may rename folders under <c>/Accounts/acc_1</c>". Register it
/// with <c>AddResourceKeyAuthorization</c>, its grants with <c>AddResourceGrantSource</c> and any partial-key expansion
/// with <c>AddResourceKeyExpander</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Who is refused first.</b> An unauthenticated caller, or one whose <see cref="IUserAuthorisationContext"/> is
/// anonymous, is refused with a 401 asking them to log in. A caller whose context is not active is refused with a 401
/// saying the account has been deactivated. No grant is read for either. A caller with no user context at all, as in an
/// app with no <see cref="IUserAuthorisationContextFactory"/>, is judged by grants.
/// </para>
/// <para>
/// <b>Scope pairs.</b> In <see cref="ResourceAuthorizationMode.Execute"/> and <see cref="ResourceAuthorizationMode.Link"/>
/// modes the request is allowed when any scope pair is: its <see cref="ResolvedScopePair.ExpandedResourceKey"/> is
/// demanded of <see cref="IResourceAccessChecker"/> for <see cref="ResourceAuthorizationRequest.Descriptor"/>, which for a
/// link is the linked operation. A pair with no key, or whose key resolved empty, cannot be judged by keys and is denied,
/// as is a request with no pairs.
/// </para>
/// <para>
/// <b>A link's own resource keys.</b> In <see cref="ResourceAuthorizationMode.Link"/> mode, when the resource implements
/// <see cref="IHaveResourceKeys"/> and the demanded key is partial, the resource key ending with it, compared
/// case-insensitively, is demanded instead, so the resource places itself without an expander.
/// </para>
/// <para>
/// <b>Listing.</b> In <see cref="ResourceAuthorizationMode.Listing"/> mode, asked when an operation is listed before any
/// resource exists (an authorisation manifest), a key whose template has a property token was cut at it, so it is only a
/// literal prefix. The grants are matched against it with <see cref="ResourceGrants.IsPrefixCompatibleWith"/>, as
/// Signature 365 matched them. That accepts two shapes execution never honours, so an operation can be listed and then
/// refused: an exact grant that is an ancestor of the prefix (<c>/Partners/p</c> for <c>/Partners/p/Users/</c>), and a
/// grant beneath the prefix that diverges from the rest of the template (<c>/Tenants/ten_1/Groups/*</c> for
/// <c>/Tenants/{TenantId}/Users</c>). A listing is never an execution decision. A key whose template has no property token
/// is complete, so it is judged as at execution.
/// </para>
/// <para>
/// A singleton, as every provider is. It resolves the scoped <see cref="IResourceAccessChecker"/> from the operation's own
/// services on each call, so the expanders and grant sources behind it may be scoped.
/// </para>
/// </remarks>
public sealed class ResourceKeyAuthorizationProvider : IResourceAuthorizationProvider
{
    /// <summary>
    /// The message a grant denial carries, which titles the 403 the caller sees. The reason, which names the keys and
    /// grants, is logged only.
    /// </summary>
    public const string DeniedMessage = "You do not have enough permissions to perform this operation";

    private static readonly ExecutionAllowed _unauthenticated = ExecutionAllowed.No(
        "Operation does not have AllowAnonymous attribute. User is unauthenticated",
        "Please log in",
        ExecutionAllowedFailureType.Authentication);

    private static readonly ExecutionAllowed _inactive = ExecutionAllowed.No(
        "Operation does not have AllowAnonymous attribute. User is inactive",
        "Your account has been deactivated",
        ExecutionAllowedFailureType.Authentication);

    private static readonly ExecutionAllowed _noScopePairs = ExecutionAllowed.No(
        "The request carries no scope pairs, so there is no resource key to judge",
        DeniedMessage,
        ExecutionAllowedFailureType.Authorisation);

    /// <inheritdoc />
    public async ValueTask<ExecutionAllowed> AuthorizeAsync(
        ApiOperationContext operationContext,
        ClaimsPrincipal principal,
        ResourceAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operationContext);

        var user = operationContext.UserAuthorisationContext;

        if (principal is not { Identity.IsAuthenticated: true } || user is { IsAnonymous: true })
        {
            return _unauthenticated;
        }

        if (user is { IsActive: false })
        {
            return _inactive;
        }

        if (request.Mode is not (ResourceAuthorizationMode.Execute or ResourceAuthorizationMode.Link or ResourceAuthorizationMode.Listing))
        {
            return Deny($"ResourceKeyAuthorizationProvider does not support the {request.Mode} mode");
        }

        var scopePairs = request.ScopePairs;

        if (scopePairs.Count == 0)
        {
            return _noScopePairs;
        }

        var checker = operationContext.ServiceProvider.GetRequiredService<IResourceAccessChecker>();
        var firstDenial = default(ExecutionAllowed);
        List<string>? reasons = null;

        for (var i = 0; i < scopePairs.Count; i++)
        {
            var result = await DecideAsync(checker, operationContext, principal, request, scopePairs[i], cancellationToken).ConfigureAwait(false);

            if (result.IsAllowed)
            {
                return result;
            }

            // A lone pair's denial is returned as it is; the reasons are only gathered once a second pair is denied
            if (i == 0)
            {
                firstDenial = result;
            }
            else
            {
                reasons ??= [firstDenial.Reason];
                reasons.Add(result.Reason);
            }
        }

        return reasons is null
            ? firstDenial
            : Deny($"None of the scope pairs is satisfied: {string.Join("; ", reasons)}");
    }

    /// <summary>
    /// Decides one scope pair: denied when it has no key to judge; otherwise its key, placed by a linked resource's own keys
    /// where it can be, demanded of the checker.
    /// </summary>
    private static ValueTask<ExecutionAllowed> DecideAsync(
        IResourceAccessChecker checker,
        ApiOperationContext operationContext,
        ClaimsPrincipal principal,
        ResourceAuthorizationRequest request,
        ResolvedScopePair scopePair,
        CancellationToken cancellationToken)
    {
        if (scopePair.ExpandedResourceKey is null)
        {
            return new ValueTask<ExecutionAllowed>(Deny(
                $"The scope pair {scopePair.Noun}/{scopePair.Verb} has no resource key, so it cannot be judged by resource grants"));
        }

        if (scopePair.ExpandedResourceKey.Length == 0)
        {
            return new ValueTask<ExecutionAllowed>(Deny(
                $"The resource key of the scope pair {scopePair.Noun}/{scopePair.Verb} ('{scopePair.ResourceKeyTemplate}') resolved empty, so it cannot be judged by resource grants"));
        }

        if (request.Mode == ResourceAuthorizationMode.Listing && HasPropertyToken(scopePair.ResourceKeyTemplate))
        {
            return IsListedAsync(operationContext, principal, request.Descriptor, scopePair, cancellationToken);
        }

        var key = new ResourceKey(scopePair.ExpandedResourceKey);

        if (request.Mode == ResourceAuthorizationMode.Link && !key.IsAbsolute && request.Resource is IHaveResourceKeys resource)
        {
            key = MatchResourceKey(key, resource);
        }

        return checker.HasAccessAsync(operationContext, principal, new ResourceDemand(request.Descriptor, key), scopePair, cancellationToken);
    }

    /// <summary>
    /// Replaces a partial key with the linked resource's own key that ends with it, compared case-insensitively, so that the
    /// resource places itself without an expander. A partial key that ends none of them is returned unchanged.
    /// </summary>
    /// <remarks>
    /// Kept as Signature 365's authoriser has it, for D10 parity: the loop does not stop at the first match, so when several
    /// keys end with the partial key the last one wins, and the suffix is not held to a segment boundary.
    /// </remarks>
    private static ResourceKey MatchResourceKey(ResourceKey partialKey, IHaveResourceKeys resource)
    {
        var matched = partialKey;

        foreach (var resourceKey in resource.ResourceKeys)
        {
            if (!string.IsNullOrEmpty(resourceKey) && resourceKey.EndsWith(partialKey.Value, StringComparison.OrdinalIgnoreCase))
            {
                matched = new ResourceKey(resourceKey);
            }
        }

        return matched;
    }

    /// <summary>
    /// Decides a listing whose key was cut at a property token: allowed when any grant from any source is not ruled out by
    /// the literal prefix the key carries.
    /// </summary>
    private static async ValueTask<ExecutionAllowed> IsListedAsync(
        ApiOperationContext operationContext,
        ClaimsPrincipal principal,
        ApiOperationDescriptor operation,
        ResolvedScopePair scopePair,
        CancellationToken cancellationToken)
    {
        var prefix = scopePair.ExpandedResourceKey!;

        foreach (var source in operationContext.ServiceProvider.GetServices<IResourceGrantSource>())
        {
            var grants = await source.GetGrantsAsync(operationContext, principal, operation, scopePair, cancellationToken).ConfigureAwait(false);

            if (grants.IsPrefixCompatibleWith(prefix))
            {
                return ExecutionAllowed.Yes;
            }
        }

        return Deny($"No grant is compatible with the listed prefix '{prefix}' of {scopePair.Noun}/{scopePair.Verb} ('{scopePair.ResourceKeyTemplate}')");
    }

    /// <summary>
    /// Whether a template has a property token, at which a listing's key was cut. A caller (<c>{Auth.X}</c>) token is
    /// resolved for a listing, so it does not count.
    /// </summary>
    private static bool HasPropertyToken(string? template)
    {
        if (template is null)
        {
            return false;
        }

        for (var start = template.IndexOf('{'); start >= 0; start = template.IndexOf('{', start + 1))
        {
            var end = template.IndexOf('}', start + 1);

            if (end < 0)
            {
                return false;
            }

            var token = template.AsSpan(start + 1, end - start - 1);
            var separator = token.IndexOf(':');
            var primary = separator >= 0 ? token[..separator] : token;

            if (!primary.Equals("Auth", StringComparison.OrdinalIgnoreCase) && !primary.StartsWith("Auth.", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static ExecutionAllowed Deny(string reason)
    {
        return ExecutionAllowed.No(reason, DeniedMessage, ExecutionAllowedFailureType.Authorisation);
    }
}

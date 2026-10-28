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
/// <b>Listing.</b> <see cref="ResourceAuthorizationMode.Listing"/> is denied until it is supported
/// (Symprex/blueprint#81). Nothing asks for it yet.
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

    private static readonly ExecutionAllowed _listingNotSupported = ExecutionAllowed.No(
        "Listing mode is not supported by ResourceKeyAuthorizationProvider until Symprex/blueprint#81",
        DeniedMessage,
        ExecutionAllowedFailureType.Authorisation);

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

        if (request.Mode == ResourceAuthorizationMode.Listing)
        {
            return _listingNotSupported;
        }

        if (request.Mode is not (ResourceAuthorizationMode.Execute or ResourceAuthorizationMode.Link))
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

    private static ExecutionAllowed Deny(string reason)
    {
        return ExecutionAllowed.No(reason, DeniedMessage, ExecutionAllowedFailureType.Authorisation);
    }
}

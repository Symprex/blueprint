using System.Security.Claims;
using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.Authorization;

/// <summary>
/// Default implementation of <see cref="IApiAuthoriserAggregator"/> that iterates all registered
/// <see cref="IApiAuthoriser"/> and <see cref="ILinkVisibilityAuthoriser"/> implementations
/// and short-circuits on the first denial.
/// </summary>
/// <remarks>
/// <para>
/// Caches, for this instance's authorisers, the result of <see cref="IOperationAuthoriser.AppliesTo"/> per operation type,
/// so the O(links × authorisers) loop only evaluates applicable authorisers after the first call for each descriptor. The
/// cache belongs to the instance, not the type: another container can register other authorisers for the same operation,
/// and a cache keyed by the operation alone would skip them.
/// </para>
/// <para>
/// What an authoriser decides is not cached: every call asks every applicable authoriser, with the resource it is given,
/// as legacy's aggregator did (Signature365 <c>8b40ecc961</c>, <c>Blueprint/Authorisation/ApiAuthoriserAggregator.cs:22-62</c>).
/// An <see cref="IApiAuthoriser"/> may decide by the resource, so the links of a list whose items a caller may act on
/// differently are each decided by their own item.
/// </para>
/// <para>
/// For an operation that declares scope pairs, the registered <see cref="IResourceAuthorizationProvider"/>s are asked
/// too, after the <see cref="IApiAuthoriser"/>s and before the <see cref="ILinkVisibilityAuthoriser"/>s, in
/// <see cref="ResourceAuthorizationMode.Link"/> mode with the keys resolved against the resource. They compose all-of, as
/// they do at execution, and a link nothing can judge is hidden, as its execution would be refused.
/// </para>
/// </remarks>
internal sealed class ApiAuthoriserAggregator : IApiAuthoriserAggregator
{
    // The caller when the context has none, which only an execution outside HTTP can lack.
    private static readonly ClaimsPrincipal _unauthenticated = new(new ClaimsIdentity());

    private readonly IApiAuthoriser[] _apiAuthorisers;
    private readonly ILinkVisibilityAuthoriser[] _linkVisibilityAuthorisers;
    private readonly IResourceAuthorizationProvider[] _resourceAuthorizationProviders;
    private readonly ILogger<ApiAuthoriserAggregator> _logger;

    // OperationType → the types of this instance's authorisers that apply to it. AppliesTo is deterministic for a given
    // descriptor, so caching is safe for these authorisers, and for no others.
    private Dictionary<Type, (Type[] ApiTypes, Type[] LinkTypes)>? _applicableTypes;

    /// <summary>
    /// Initialises a new instance of the <see cref="ApiAuthoriserAggregator"/> class.
    /// </summary>
    /// <param name="apiAuthorisers">All registered combined authoriser implementations.</param>
    /// <param name="linkVisibilityAuthorisers">All registered link-visibility-only authoriser implementations.</param>
    /// <param name="resourceAuthorizationProviders">All registered resource authorization providers, in registration order.</param>
    /// <param name="logger">Logger for trace-level diagnostics.</param>
    public ApiAuthoriserAggregator(
        IEnumerable<IApiAuthoriser> apiAuthorisers,
        IEnumerable<ILinkVisibilityAuthoriser> linkVisibilityAuthorisers,
        IEnumerable<IResourceAuthorizationProvider> resourceAuthorizationProviders,
        ILogger<ApiAuthoriserAggregator> logger)
    {
        // Materialize once to avoid repeated enumeration
        this._apiAuthorisers = apiAuthorisers.ToArray();
        this._linkVisibilityAuthorisers = linkVisibilityAuthorisers.ToArray();
        this._resourceAuthorizationProviders = resourceAuthorizationProviders.ToArray();
        this._logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<ExecutionAllowed> CanShowLinkAsync(
        ApiOperationContext operationContext,
        ApiOperationDescriptor descriptor,
        object? resource)
    {
        this._applicableTypes ??= new Dictionary<Type, (Type[] ApiTypes, Type[] LinkTypes)>();

        if (!this._applicableTypes.TryGetValue(descriptor.OperationType, out var applicable))
        {
            applicable = (
                this._apiAuthorisers.Where(a => a.AppliesTo(descriptor)).Select(static a => a.GetType()).ToArray(),
                this._linkVisibilityAuthorisers.Where(l => l.AppliesTo(descriptor)).Select(static l => l.GetType()).ToArray());
            this._applicableTypes[descriptor.OperationType] = applicable;
        }

        var (applicableApiTypes, applicableLinkTypes) = applicable;

        // Asked for this resource, whatever was decided for another of the same operation.
        var apiResult = await this.EvaluateApiAuthorisersAsync(operationContext, descriptor, resource, applicableApiTypes).ConfigureAwait(false);

        if (!apiResult.IsAllowed)
        {
            return apiResult;
        }

        var providerResult = await this.EvaluateResourceAuthorizationProvidersAsync(operationContext, descriptor, resource).ConfigureAwait(false);

        if (!providerResult.IsAllowed || applicableLinkTypes.Length == 0)
        {
            return providerResult;
        }

        // Evaluate resource-dependent state checkers inline (avoids FilterByTypes allocation)
        var traceLogEnabled = this._logger.IsEnabled(LogLevel.Trace);

        foreach (var checker in this._linkVisibilityAuthorisers)
        {
            if (!IsApplicableType(checker, applicableLinkTypes))
            {
                continue;
            }

            var linkResult = await checker.CanShowLinkAsync(operationContext, descriptor, resource!).ConfigureAwait(false);

            if (!linkResult.IsAllowed)
            {
                if (traceLogEnabled)
                {
                    this._logger.LogTrace(
                        "Link check failed. type={OperationType} resource_type={ResourceType} reason={Reason} authoriser={AuthoriserType}",
                        descriptor.OperationType.Name,
                        resource?.GetType().Name,
                        linkResult.Reason,
                        checker.GetType().Name);
                }

                return linkResult;
            }
        }

        if (traceLogEnabled)
        {
            this._logger.LogTrace(
                "Link check succeeded. type={OperationType} resource_type={ResourceType}",
                descriptor.OperationType.Name,
                resource?.GetType().Name);
        }

        return ExecutionAllowed.Yes;
    }

    /// <summary>
    /// Evaluates only the <see cref="IApiAuthoriser"/> portion of link auth. Separated so
    /// that results can be cached independently of resource-dependent state-based checkers.
    /// </summary>
    private async ValueTask<ExecutionAllowed> EvaluateApiAuthorisersAsync(
        ApiOperationContext operationContext,
        ApiOperationDescriptor descriptor,
        object? resource,
        Type[] applicableApiTypes)
    {
        if (applicableApiTypes.Length == 0)
        {
            return ExecutionAllowed.Yes;
        }

        var traceLogEnabled = this._logger.IsEnabled(LogLevel.Trace);

        foreach (var checker in this._apiAuthorisers)
        {
            if (!IsApplicableType(checker, applicableApiTypes))
            {
                continue;
            }

            var result = await checker.CanShowLinkAsync(operationContext, descriptor, resource!).ConfigureAwait(false);

            if (!result.IsAllowed)
            {
                if (traceLogEnabled)
                {
                    this._logger.LogTrace(
                        "Link check failed. type={OperationType} resource_type={ResourceType} reason={Reason} authoriser={AuthoriserType}",
                        descriptor.OperationType.Name,
                        resource?.GetType().Name,
                        result.Reason,
                        checker.GetType().Name);
                }

                return result;
            }
        }

        if (traceLogEnabled)
        {
            this._logger.LogTrace(
                "Link check succeeded. type={OperationType} resource_type={ResourceType}",
                descriptor.OperationType.Name,
                resource?.GetType().Name);
        }

        return ExecutionAllowed.Yes;
    }

    /// <summary>
    /// Asks the resource authorization providers whether a link to <paramref name="descriptor"/> may be written on
    /// <paramref name="resource"/>, with the operation's keys resolved against the resource by the generated
    /// <see cref="OperationLinkAuthorizationFeatureData"/>.
    /// </summary>
    /// <remarks>
    /// An operation without scope pairs, or a link without a resource, is not the providers' to decide here: a link with no
    /// resource is listing mode's (#81). Otherwise the link is refused when no key resolver handles the resource's type or
    /// no provider is registered, as the operation's execution would be.
    /// </remarks>
    private async ValueTask<ExecutionAllowed> EvaluateResourceAuthorizationProvidersAsync(
        ApiOperationContext operationContext,
        ApiOperationDescriptor descriptor,
        object? resource)
    {
        if (resource is null
            || !descriptor.TryGetFeatureData<OperationAuthorizationFeatureData>(out var authorization)
            || authorization.ScopePairs.IsDefaultOrEmpty)
        {
            return ExecutionAllowed.Yes;
        }

        var principal = operationContext.UserPrincipal ?? _unauthenticated;

        if (!descriptor.TryGetFeatureData<OperationLinkAuthorizationFeatureData>(out var linkAuthorization)
            || linkAuthorization.Resolve(resource, name => CallerValue(operationContext, principal, name)) is not { } scopePairs)
        {
            this._logger.LogWarning(
                "Link hidden: {OperationType} declares scope pairs but has no key resolver for the resource type {ResourceType}",
                descriptor.OperationType.Name,
                resource.GetType().Name);

            return ExecutionAllowed.No(
                $"{descriptor.OperationType.Name} cannot resolve its resource keys against {resource.GetType().Name}",
                "You do not have permission to perform this operation",
                ExecutionAllowedFailureType.Authorisation);
        }

        if (this._resourceAuthorizationProviders.Length == 0)
        {
            this._logger.LogError(
                "Link hidden: {OperationType} declares [Resource] scope pairs but no IResourceAuthorizationProvider is registered. Register one with AddResourceAuthorizationProvider",
                descriptor.OperationType.Name);

            return ExecutionAllowed.No(
                $"No IResourceAuthorizationProvider is registered to judge {descriptor.OperationType.Name}",
                "You do not have permission to perform this operation",
                ExecutionAllowedFailureType.Authorisation);
        }

        var request = ResourceAuthorizationRequest.Link(descriptor, scopePairs, resource);

        foreach (var provider in this._resourceAuthorizationProviders)
        {
            var result = await provider.AuthorizeAsync(operationContext, principal, request, operationContext.OperationCancelled).ConfigureAwait(false);

            if (!result.IsAllowed)
            {
                if (this._logger.IsEnabled(LogLevel.Trace))
                {
                    this._logger.LogTrace(
                        "Link check failed. type={OperationType} resource_type={ResourceType} reason={Reason} provider={ProviderType}",
                        descriptor.OperationType.Name,
                        resource.GetType().Name,
                        result.Reason,
                        provider.GetType().Name);
                }

                return result;
            }
        }

        return ExecutionAllowed.Yes;
    }

    /// <summary>
    /// Gives the caller's value for an <c>{Auth.X}</c> token, as an execution's generated check resolves it: a property of
    /// <see cref="IUserAuthorisationContext"/> for the names the generator knows, falling back to the principal's claim of
    /// that name.
    /// </summary>
    private static string? CallerValue(ApiOperationContext context, ClaimsPrincipal principal, string name)
    {
        var user = context.UserAuthorisationContext;

        var known = user is null
            ? null
            : name.Equals("AccountId", StringComparison.OrdinalIgnoreCase) ? user.AccountId
            : name.Equals("Id", StringComparison.OrdinalIgnoreCase) ? user.Id
            : name.Equals("IdentityId", StringComparison.OrdinalIgnoreCase) ? user.IdentityId
            : name.Equals("Email", StringComparison.OrdinalIgnoreCase) ? user.Email
            : name.Equals("Name", StringComparison.OrdinalIgnoreCase) ? user.Name
            : null;

        return known ?? principal.FindFirst(name)?.Value;
    }

    /// <summary>
    /// Checks whether an authoriser instance's type is in the applicable types array.
    /// Uses a simple linear scan because applicable arrays are typically 1-3 entries.
    /// </summary>
    private static bool IsApplicableType<T>(T instance, Type[] applicableTypes)
    {
        var type = instance!.GetType();
        foreach (var applicable in applicableTypes)
        {
            if (type == applicable)
            {
                return true;
            }
        }

        return false;
    }
}

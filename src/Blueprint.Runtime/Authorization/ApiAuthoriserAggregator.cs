using System.Collections.Concurrent;
using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.Authorization;

/// <summary>
/// Default implementation of <see cref="IApiAuthoriserAggregator"/> that iterates all registered
/// <see cref="IApiAuthoriser"/> and <see cref="ILinkVisibilityAuthoriser"/> implementations
/// and short-circuits on the first denial.
/// </summary>
/// <remarks>
/// Caches the result of <see cref="IOperationAuthoriser.AppliesTo"/> per operation type so the
/// O(links × authorisers) loop only evaluates applicable authorisers, skipping the per-request
/// filtering cost after the first call for each descriptor.
/// </remarks>
internal sealed class ApiAuthoriserAggregator : IApiAuthoriserAggregator
{
    private readonly IApiAuthoriser[] _apiAuthorisers;
    private readonly ILinkVisibilityAuthoriser[] _linkVisibilityAuthorisers;
    private readonly ILogger<ApiAuthoriserAggregator> _logger;

    // Cache keyed by OperationType → applicable authoriser types for that operation.
    // AppliesTo is deterministic for a given descriptor, so caching is safe.
    // Static because this is descriptor-level metadata that does not vary per scope.
    private static readonly ConcurrentDictionary<Type, (Type[] ApiTypes, Type[] LinkTypes)> _applicableTypeCache = new();

    // Per-request cache for link auth decisions where no state-based (resource-dependent)
    // checkers apply. Keyed by operation type — safe because permission-based auth depends
    // only on the user principal, not the resource instance.
    private Dictionary<Type, ExecutionAllowed>? _resourceIndependentLinkAuthCache;

    /// <summary>
    /// Initialises a new instance of the <see cref="ApiAuthoriserAggregator"/> class.
    /// </summary>
    /// <param name="apiAuthorisers">All registered combined authoriser implementations.</param>
    /// <param name="linkVisibilityAuthorisers">All registered link-visibility-only authoriser implementations.</param>
    /// <param name="logger">Logger for trace-level diagnostics.</param>
    public ApiAuthoriserAggregator(
        IEnumerable<IApiAuthoriser> apiAuthorisers,
        IEnumerable<ILinkVisibilityAuthoriser> linkVisibilityAuthorisers,
        ILogger<ApiAuthoriserAggregator> logger)
    {
        // Materialize once to avoid repeated enumeration
        this._apiAuthorisers = apiAuthorisers.ToArray();
        this._linkVisibilityAuthorisers = linkVisibilityAuthorisers.ToArray();
        this._logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<ExecutionAllowed> CanShowLinkAsync(
        ApiOperationContext operationContext,
        ApiOperationDescriptor descriptor,
        object? resource)
    {
        // Use a static type-based cache for AppliesTo results so scoped instances
        // can still benefit from cross-request caching of the deterministic check.
        var (applicableApiTypes, applicableLinkTypes) = _applicableTypeCache.GetOrAdd(
            descriptor.OperationType,
            static (_, state) =>
            {
                var (desc, api, link) = state;

                var apiList = new List<Type>(api.Length);
                foreach (var a in api)
                {
                    if (a.AppliesTo(desc))
                    {
                        apiList.Add(a.GetType());
                    }
                }

                var linkList = new List<Type>(link.Length);
                foreach (var l in link)
                {
                    if (l.AppliesTo(desc))
                    {
                        linkList.Add(l.GetType());
                    }
                }

                return (apiList.ToArray(), linkList.ToArray());
            },
            (descriptor, this._apiAuthorisers, this._linkVisibilityAuthorisers));

        // When no state-based (resource-dependent) link checkers apply, the auth result
        // depends only on the user principal + operation descriptor. Cache per-request to
        // avoid redundant permission evaluations when populating collections of the same type.
        if (applicableLinkTypes.Length == 0)
        {
            this._resourceIndependentLinkAuthCache ??= new Dictionary<Type, ExecutionAllowed>();

            if (this._resourceIndependentLinkAuthCache.TryGetValue(descriptor.OperationType, out var cached))
            {
                return cached;
            }

            var result = await this.EvaluateApiAuthorisersAsync(operationContext, descriptor, resource, applicableApiTypes).ConfigureAwait(false);
            this._resourceIndependentLinkAuthCache[descriptor.OperationType] = result;
            return result;
        }

        // State-based checkers exist — permission check is still principal-only so cache it,
        // then evaluate resource-dependent state checkers per instance.
        this._resourceIndependentLinkAuthCache ??= new Dictionary<Type, ExecutionAllowed>();

        ExecutionAllowed apiResult;
        if (this._resourceIndependentLinkAuthCache.TryGetValue(descriptor.OperationType, out var cachedApiResult))
        {
            apiResult = cachedApiResult;
        }
        else
        {
            apiResult = await this.EvaluateApiAuthorisersAsync(operationContext, descriptor, resource, applicableApiTypes).ConfigureAwait(false);
            this._resourceIndependentLinkAuthCache[descriptor.OperationType] = apiResult;
        }

        if (!apiResult.IsAllowed)
        {
            return apiResult;
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

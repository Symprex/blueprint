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

        // Filter the per-scope instances to only those whose types are applicable.
        var applicableApi = FilterByTypes(this._apiAuthorisers, applicableApiTypes);
        var applicableLink = FilterByTypes(this._linkVisibilityAuthorisers, applicableLinkTypes);

        var traceLogEnabled = this._logger.IsEnabled(LogLevel.Trace);

        foreach (var checker in applicableApi)
        {
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

        foreach (var checker in applicableLink)
        {
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
    /// Filters an array of instances to only those whose runtime type appears in the
    /// cached applicable-types array. Avoids allocations when all instances match.
    /// </summary>
    private static T[] FilterByTypes<T>(T[] instances, Type[] applicableTypes)
    {
        if (applicableTypes.Length == 0)
        {
            return [];
        }

        // Fast path: if counts match, all are applicable (common case).
        if (instances.Length == applicableTypes.Length)
        {
            return instances;
        }

        var result = new List<T>(applicableTypes.Length);
        foreach (var instance in instances)
        {
            var type = instance!.GetType();
            foreach (var applicable in applicableTypes)
            {
                if (type == applicable)
                {
                    result.Add(instance);
                    break;
                }
            }
        }

        return result.ToArray();
    }
}

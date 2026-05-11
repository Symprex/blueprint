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

    // Cache keyed by OperationType → applicable authorisers for that operation.
    // AppliesTo is deterministic for a given descriptor, so caching is safe.
    private readonly ConcurrentDictionary<Type, (IApiAuthoriser[] Api, ILinkVisibilityAuthoriser[] Link)> _applicableCache = new();

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
        var (applicableApi, applicableLink) = this._applicableCache.GetOrAdd(
            descriptor.OperationType,
            static (_, state) =>
            {
                var (desc, api, link) = state;

                var apiList = new List<IApiAuthoriser>(api.Length);
                foreach (var a in api)
                {
                    if (a.AppliesTo(desc))
                    {
                        apiList.Add(a);
                    }
                }

                var linkList = new List<ILinkVisibilityAuthoriser>(link.Length);
                foreach (var l in link)
                {
                    if (l.AppliesTo(desc))
                    {
                        linkList.Add(l);
                    }
                }

                return (apiList.ToArray(), linkList.ToArray());
            },
            (descriptor, this._apiAuthorisers, this._linkVisibilityAuthorisers));

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
}

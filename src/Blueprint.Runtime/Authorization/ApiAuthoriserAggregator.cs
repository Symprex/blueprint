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
/// </remarks>
internal sealed class ApiAuthoriserAggregator : IApiAuthoriserAggregator
{
    private readonly IApiAuthoriser[] _apiAuthorisers;
    private readonly ILinkVisibilityAuthoriser[] _linkVisibilityAuthorisers;
    private readonly ILogger<ApiAuthoriserAggregator> _logger;

    // OperationType → the types of this instance's authorisers that apply to it. AppliesTo is deterministic for a given
    // descriptor, so caching is safe for these authorisers, and for no others.
    private Dictionary<Type, (Type[] ApiTypes, Type[] LinkTypes)>? _applicableTypes;

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

        if (!apiResult.IsAllowed || applicableLinkTypes.Length == 0)
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

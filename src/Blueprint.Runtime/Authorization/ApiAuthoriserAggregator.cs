using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.Authorization;

/// <summary>
/// Default implementation of <see cref="IApiAuthoriserAggregator"/> that iterates all registered
/// <see cref="IApiAuthoriser"/> implementations and short-circuits on the first denial.
/// </summary>
internal sealed class ApiAuthoriserAggregator : IApiAuthoriserAggregator
{
    private readonly IEnumerable<IApiAuthoriser> _apiAuthorisers;
    private readonly ILogger<ApiAuthoriserAggregator> _logger;

    /// <summary>
    /// Initialises a new instance of the <see cref="ApiAuthoriserAggregator"/> class.
    /// </summary>
    /// <param name="apiAuthorisers">All registered authoriser implementations.</param>
    /// <param name="logger">Logger for trace-level diagnostics.</param>
    public ApiAuthoriserAggregator(
        IEnumerable<IApiAuthoriser> apiAuthorisers,
        ILogger<ApiAuthoriserAggregator> logger)
    {
        this._apiAuthorisers = apiAuthorisers;
        this._logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<ExecutionAllowed> CanShowLinkAsync(
        ApiOperationContext operationContext,
        ApiOperationDescriptor descriptor,
        object? resource)
    {
        var traceLogEnabled = this._logger.IsEnabled(LogLevel.Trace);

        foreach (var checker in this._apiAuthorisers)
        {
            if (!checker.AppliesTo(descriptor))
            {
                continue;
            }

            var result = await checker.CanShowLinkAsync(operationContext, descriptor, resource!);

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
